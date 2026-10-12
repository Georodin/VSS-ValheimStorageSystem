using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace VSS;
internal static class VssTransfer
{
    private static bool _busy;
    public static bool Busy { get => _busy || VssTransactions.Busy; private set => _busy = value; }
    private static bool _cancel;
    private static int _lastMoved;
    private static string _error;
    public static void Cancel() => _cancel = true;
    public static void Deposit(Player player, VssAccessStand stand, ItemDrop.ItemData source, int amount)
    {
        if (Busy || source == null || source.m_equipped) { VssUi.Status("Unequip items before storing them."); return; }
        Start(Run(player, stand, source, new VssItemKey(source), amount, true, -1, -1));
    }
    public static void Withdraw(Player player, VssAccessStand stand, VssItemRow row, int amount, int x = -1, int y = -1)
    {
        if (Busy || row == null) return;
        Start(Run(player, stand, null, row.Key, Math.Min(amount, row.MaxStack), false, x, y));
    }
    internal static void Consume(Player player, VssAccessStand stand, VssItemRow row)
    {
        if (Busy || row == null) return;
        if (row.Sample.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) { VssUi.Status("Right-click stored food or mead to consume one."); return; }
        Start(RunConsume(player, stand, row));
    }
    private static IEnumerator RunConsume(Player player, VssAccessStand stand, VssItemRow row)
    {
        var used = false;
        try
        {
            if (!Valid(player, stand)) yield break;
            foreach (var chest in VssStorageScanner.Scan(stand.transform.position, player).Containers)
            {
                if (!Valid(player, stand) || used) break;
                if (chest.Container == null || !chest.Inventory.GetAllItems().Any(row.Key.Matches)) continue;
                var attempted = false;
                yield return WithLease(chest.Container, player, stand, () =>
                {
                    var item = chest.Inventory.GetAllItems().FirstOrDefault(row.Key.Matches);
                    if (item == null) return 0;
                    attempted = true;
                    var count = item.m_stack;
                    player.UseItem(chest.Inventory, item, true);
                    used = item.m_stack < count || !chest.Inventory.ContainsItem(item);
                    return used ? 1 : 0;
                });
                // A native cooldown/full-food rejection should not try another chest.
                if (attempted) break;
            }
        }
        finally { Busy = false; VssUi.Status(used ? "Consumed " + row.Key.DisplayName + "." : "Item could not be consumed, or storage is busy."); VssUi.RefreshNow(); }
    }
    private static void Start(IEnumerator routine)
    {
        if (Busy) return;
        if (!VssSettings.Ready) { VssUi.Status("Waiting for VSS on the host. All players and the server need the same VSS version."); return; }
        Busy = true; _cancel = false; _error = "";
        VssPlugin.Instance.StartCoroutine(routine);
    }
    private static bool Valid(Player player, VssAccessStand stand) => !_cancel && VssUi.IsOpen &&
        player != null && stand != null && !player.IsDead() && !player.IsTeleporting() &&
        (player.transform.position - stand.transform.position).sqrMagnitude <= 25f &&
        PrivateArea.CheckAccess(stand.transform.position, 0f, false, false);
    private static IEnumerator Run(Player player, VssAccessStand stand, ItemDrop.ItemData source, VssItemKey key,
        int requested, bool deposit, int x, int y)
    {
        var moved = 0;
        try
        {
            VssUi.Status("Moving items…");
            if (!Valid(player, stand)) yield break;
            var inventory = player.GetInventory();
            var containers = VssStorageScanner.Scan(stand.transform.position, player).Containers;
            if (deposit)
            {
                requested = Math.Min(requested, source.m_stack);
                // First fill existing stacks across the entire network, then allocate new slots by preference.
                var ordered = containers.OrderBy(c => VssPreferences.Rank(c.Container, source)).ThenBy(c => c.Distance).ToList();
                for (var phase = 0; phase < 2 && moved < requested; phase++)
                {
                    foreach (var chest in ordered)
                    {
                        if (!Valid(player, stand) || moved >= requested || !inventory.ContainsItem(source)) break;
                        var stacksOnly = phase == 0;
                        if (chest.Container == null || VssInventoryOps.Capacity(chest.Inventory, source, stacksOnly) <= 0) continue;
                        var left = requested - moved;
                        yield return WithLease(chest.Container, player, stand, () =>
                            VssInventoryOps.Move(inventory, chest.Inventory, source, left, stacksOnly));
                        moved += _lastMoved;
                    }
                }
            }
            else
            {
                foreach (var chest in containers)
                {
                    if (!Valid(player, stand) || moved >= requested) break;
                    if (chest.Container == null || !chest.Inventory.GetAllItems().Any(key.Matches)) continue;
                    var left = requested - moved;
                    yield return WithLease(chest.Container, player, stand, () =>
                    {
                        var count = 0;
                        foreach (var item in chest.Inventory.GetAllItems().ToList())
                        {
                            if (count >= left) break;
                            if (key.Matches(item)) count += VssInventoryOps.Move(chest.Inventory, inventory, item, left - count, false, x, y);
                        }
                        return count;
                    });
                    moved += _lastMoved;
                }
            }
        }
        finally
        {
            Busy = false;
            VssUi.Status(moved > 0 ? $"{(deposit ? "Stored" : "Took")} {moved:N0} {key.DisplayName}." :
                string.IsNullOrEmpty(_error) ? "No room, no matching items, or the chests are busy." : _error);
            VssUi.RefreshNow();
        }
    }
    private static IEnumerator WithLease(Container chest, Player player, VssAccessStand stand, Func<int> operation)
    {
        _lastMoved = 0;
        if (chest == null || !VssStorageScanner.CanAccess(chest, player)) yield break;
        var bridge = chest.GetComponent<VssChestBridge>();
        if (bridge == null) { _error = "A chest owner is missing VSS. Install the same version on every player and the server."; yield break; }
        var token = bridge.Begin(player, stand);
        try
        {
            var deadline = Time.realtimeSinceStartup + 4f;
            while (bridge != null && !bridge.Rejected && !(bridge.Granted && bridge.CanCommit(token)) && Time.realtimeSinceStartup < deadline)
            {
                if (!Valid(player, stand)) yield break;
                yield return null;
            }
            if (bridge != null && !bridge.Rejected && !bridge.Granted)
                _error = "Chest owner did not respond. Check the connection and VSS version on every player and the server.";
            if (bridge == null || bridge.Rejected || !bridge.Granted || !bridge.CanCommit(token) || !Valid(player, stand) ||
                chest == null || !VssStorageScanner.CanAccess(chest, player) ||
                (chest.transform.position - stand.transform.position).sqrMagnitude > VssSettings.Radius * VssSettings.Radius) yield break;
            Commit(chest, operation);
        }
        finally { if (bridge != null) bridge.Release(token); }
    }
    private static void Commit(Container chest, Func<int> operation)
    {
        try
        {
            VssGame.Load(chest);
            chest.SetInUse(true);
            _lastMoved = operation();
            VssGame.Save(chest);
        }
        catch (Exception ex)
        {
            _error = "Transfer failed; see BepInEx/LogOutput.log.";
            VssPlugin.Log.LogError(ex);
        }
        finally { chest.SetInUse(false); }
    }
}
