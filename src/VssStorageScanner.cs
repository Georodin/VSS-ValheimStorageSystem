using System;
using System.Collections.Generic;
using UnityEngine;

namespace VSS;
internal static class VssStorageScanner
{
    internal static readonly HashSet<Container> Containers = new();
    private static readonly Dictionary<string, bool> ModdedPieces = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedModded = new(StringComparer.Ordinal);
    public static bool Supported(Container container) => Supported(container, true);
    // Player.PlacePiece sets the creator after Awake, so registration skips that check; access and transfers still apply it.
    internal static bool Supported(Container container, bool requireCreator)
    {
        if (container == null || container.m_wagon != null || container.m_autoDestroyEmpty) return false;
        var view = VssGame.View(container);
        if (view == null || !view.IsValid()) return false;
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(view.GetZDO().GetPrefab()) : null;
        var name = prefab != null ? prefab.name : container.gameObject.name.Replace("(Clone)", "").Trim();
        if (VssPlugin.ModConfig.Excluded.Contains(name)) return false;
        if (name.StartsWith("piece_chest", StringComparison.Ordinal) || name == "piece_barrel") return true;
        if (!VssPlugin.ModConfig.ModdedContainers.Value || !ModdedPiece(container, view, name)) return false;
        if (!requireCreator) return true;
        if (view.GetZDO().GetLong(ZDOVars.s_creator, 0L) == 0L) return false;
        if (LoggedModded.Add(name)) VssPlugin.Log.LogInfo($"Linking modded container {name}. Add it to ExcludedContainers to leave it out.");
        return true;
    }
    // Modded storage (e.g. OdinsKingdom crates) uses its own prefab names. Accept standalone build pieces only:
    // ship and cart storage sits on a child of the vehicle's root, and graves and the Obliterator (which destroys its contents) are excluded.
    private static bool ModdedPiece(Container container, ZNetView view, string name)
    {
        if (ModdedPieces.TryGetValue(name, out var supported)) return supported;
        supported = view.gameObject == container.gameObject && view.GetComponent<Piece>() != null &&
            view.GetComponent<Ship>() == null && view.GetComponent<TombStone>() == null && view.GetComponent<Incinerator>() == null;
        ModdedPieces[name] = supported;
        return supported;
    }
    public static bool CanAccess(Container container, Player player) => player != null && Supported(container) &&
        VssGame.CheckAccess(container, player.GetPlayerID()) &&
        (!container.m_checkGuardStone || PrivateArea.CheckAccess(container.transform.position, 0f, false, false));
    public static VssSnapshot Scan(Vector3 origin, Player player)
    {
        var snapshot = new VssSnapshot();
        var rows = new Dictionary<string, VssItemRow>(StringComparer.Ordinal);
        var radius2 = VssSettings.Radius * VssSettings.Radius;
        Containers.RemoveWhere(c => c == null);
        foreach (var container in Containers)
        {
            if (!container.gameObject.activeInHierarchy || (container.transform.position - origin).sqrMagnitude > radius2 || !CanAccess(container, player)) continue;
            var inventory = container.GetInventory();
            if (inventory == null) continue;
            if (!container.IsInUse()) VssGame.Load(container);
            snapshot.Containers.Add(new VssContainerRef { Container = container, Distance = Vector3.Distance(origin, container.transform.position) });
            snapshot.TotalSlots += inventory.GetWidth() * inventory.GetHeight(); snapshot.UsedSlots += inventory.NrOfItems();
            if (container.IsInUse() || VssChestBridge.IsLeased(VssGame.View(container).GetZDO())) snapshot.BusyContainers++;
            foreach (var item in inventory.GetAllItems())
            {
                var key = new VssItemKey(item);
                if (!rows.TryGetValue(key.Id, out var row))
                {
                    row = new VssItemRow { Key = key, Sample = item.Clone(), MaxStack = Math.Max(1, item.m_shared.m_maxStackSize) };
                    rows.Add(key.Id, row);
                }
                row.Total += item.m_stack; row.StackCount++;
            }
        }
        snapshot.Containers.Sort((a, b) => a.Distance.CompareTo(b.Distance)); snapshot.Rows.AddRange(rows.Values);
        return snapshot;
    }
    public static bool Linked(Container container)
    {
        if (!Supported(container)) return false;
        foreach (var stand in VssAccessStand.Instances)
            if (stand != null && (stand.transform.position - container.transform.position).sqrMagnitude <= VssSettings.Radius * VssSettings.Radius) return true;
        return false;
    }
}
