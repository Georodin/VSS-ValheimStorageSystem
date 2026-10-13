using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace VSS;

public sealed partial class VssRefillBridge
{
    internal const string CollectKey = "vss_object_auto_collect";
    private string _collectRequest;
    private long _collectOwner;
    private float _collectUntil;
    private static float _nextCollect;
    private static int _collectCursor;
    private static readonly MethodInfo SetCookingSlot = AccessTools.Method(typeof(CookingStation), "SetSlot");
    private static readonly object EmptySlotStatus = Enum.ToObject(SetCookingSlot.GetParameters()[3].ParameterType, 0);
    internal bool AutoCollect => _view != null && _view.IsValid() && _view.GetZDO().GetBool(CollectKey, false);
    internal bool CanAutoCollect => _smelter != null && _smelter.m_conversion.Any(c => c.m_to != null) ||
        _cooking != null && _cooking.m_slots != null && _cooking.m_slots.Length > 0 && _cooking.m_conversion.Any(c => c.m_to != null);
    internal static bool CollectEnabled(ZDO controller, ZDO target) => controller != null && target != null &&
        controller.GetBool(VssAccessStand.CollectKey, false) && target.GetBool(CollectKey, false);
    internal static bool CollectSupportedPrefab(int hash)
    {
        var prefab = ZNetScene.instance?.GetPrefab(hash);
        return prefab != null && (prefab.GetComponentInChildren<Smelter>(true) != null || prefab.GetComponentInChildren<CookingStation>(true) != null);
    }
    private void RegisterCollectRpc()
    {
        _view.Register<long, bool, string>("VSS_SetObjectCollect", SetAutoCollect);
        _view.Register<string, bool, bool>("VSS_ObjectCollectResult", CollectReply);
    }
    private bool CanToggleCollect(Player player) => player != null && !player.IsDead() && !player.IsTeleporting() &&
        VssSettings.Ready && _view != null && _view.IsValid() && CanAutoCollect &&
        (player.transform.position - transform.position).sqrMagnitude <= 25f &&
        VssAccessStand.HasWardAccess(player.GetPlayerID(), transform.position) &&
        VssAccessStand.Find(transform.position, player.GetPlayerID()) != null;
    internal bool ToggleAutoCollect(Player player)
    {
        if (player != Player.m_localPlayer || !CanToggleCollect(player)) return false;
        if (_collectRequest != null && Time.unscaledTime < _collectUntil) return true;
        _collectRequest = Guid.NewGuid().ToString("N"); _collectOwner = _view.GetZDO().GetOwner(); _collectUntil = Time.unscaledTime + 3f;
        _view.InvokeRPC("VSS_SetObjectCollect", player.GetPlayerID(), !AutoCollect, _collectRequest);
        return true;
    }
    private void SetAutoCollect(long sender, long actor, bool enabled, string token)
    {
        if (_view == null || !_view.IsValid() || !_view.IsOwner() || string.IsNullOrEmpty(token) || token.Length > 64) return;
        var player = Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetPlayerID() == actor);
        var pv = player != null ? player.GetComponent<ZNetView>() : null;
        var accepted = pv != null && pv.IsValid() && pv.GetZDO().GetOwner() == sender && CanToggleCollect(player);
        if (accepted)
        {
            _view.GetZDO().Set(CollectKey, enabled);
            VssGame.ForceSend(ZDOMan.instance, _view.GetZDO().m_uid);
        }
        _view.InvokeRPC(sender, "VSS_ObjectCollectResult", token, accepted, AutoCollect);
    }
    private void CollectReply(long sender, string token, bool accepted, bool enabled)
    {
        if (sender != _collectOwner || token != _collectRequest) return;
        _collectRequest = null;
        var player = Player.m_localPlayer;
        if (player == null) return;
        var stand = VssAccessStand.Find(transform.position, player.GetPlayerID());
        player.Message(MessageHud.MessageType.TopLeft, !accepted ? "Cannot change VSS auto-collect here." :
            "VSS auto-collect: " + (enabled ? "On" : "Off") + (enabled && (stand == null || !stand.AutoCollect) ? " (collection master Off)." : "."));
    }
    internal static void CollectTick()
    {
        if (!VssSettings.Ready || VssTransfer.Busy || Time.unscaledTime < _nextCollect || ZNetScene.instance == null) return;
        _nextCollect = Time.unscaledTime + 0.5f;
        Instances.RemoveWhere(s => s == null);
        var targets = Instances.Where(s => s._view != null && s._view.IsValid() && s._view.IsOwner() &&
            s.gameObject.activeInHierarchy && s.AutoCollect && s.CanAutoCollect)
            .OrderBy(s => s._view.GetZDO().m_uid.ToString(), StringComparer.Ordinal).ToList();
        for (var n = 0; n < targets.Count; n++)
        {
            var index = (_collectCursor + n) % targets.Count; var target = targets[index];
            var stand = VssAccessStand.FindAutomatic(target.transform.position);
            if (stand == null || !stand.AutoCollect) continue;
            var context = new VssContext { Stand = stand, Actor = stand.GetComponent<Piece>().GetCreator(), Target = target._view, Mode = VssAccessMode.AutomaticCollect };
            if (target.RunCollect(context)) { _collectCursor = (index + 1) % targets.Count; return; }
        }
    }
    internal ItemDrop.ItemData CookedOutput(int slot, out string name, out int status, out bool cheated)
    {
        name = _view.GetZDO().GetString("slot" + slot);
        status = _view.GetZDO().GetInt("slotstatus" + slot);
        cheated = _view.GetZDO().GetBool(ZDOVars.s_cheatedQueued + slot);
        if (string.IsNullOrEmpty(name) || status == 0) return null;
        var itemName = name;
        var drop = (_cooking.m_overCookedItem != null && itemName == _cooking.m_overCookedItem.name ? _cooking.m_overCookedItem :
            _cooking.m_conversion.Select(c => c.m_to).FirstOrDefault(d => d != null && d.name == itemName));
        if (drop == null) return null;
        var item = drop.m_itemData.Clone(); item.m_dropPrefab = drop.gameObject; item.m_stack = 1; item.m_equipped = false;
        item.m_worldLevel = (byte)Game.m_worldLevel; item.m_cheated = cheated;
        if (_cooking.m_spawnFullDurability) item.m_durability = item.m_shared.m_maxDurability;
        return item;
    }
    internal bool CommitCooked(VssContext context, int slot, Inventory destination)
    {
        if (!context.Valid() || _cooking == null || slot < 0 || slot >= _cooking.m_slots.Length) return false;
        var item = CookedOutput(slot, out _, out _, out _);
        if (item == null || VssInventoryOps.Capacity(destination, item) < 1) return false;
        var source = new Inventory("VSS cooking output", null, 1, 1); source.GetAllItems().Add(item);
        if (VssInventoryOps.Move(source, destination, item, 1) != 1) return false;
        // No yield between the leased deposit and clearing the owner-controlled native slot.
        SetCookingSlot.Invoke(_cooking, new object[] { slot, "", 0f, EmptySlotStatus, false });
        _view.InvokeRPC(ZNetView.Everybody, "RPC_SetSlotVisual", slot, "");
        VssGame.ForceSend(ZDOMan.instance, _view.GetZDO().m_uid);
        return true;
    }
    internal bool RunCollect(VssContext context)
    {
        if (VssTransfer.Busy || !context.Valid()) return false;
        if (_cooking != null)
        {
            for (var slot = 0; slot < _cooking.m_slots.Length; slot++)
            {
                var item = CookedOutput(slot, out _, out _, out _);
                if (item == null) continue;
                var sources = VssStorageScanner.Sources(context);
                var destination = Destination(sources, item);
                if (destination == null) return false;
                var selected = slot;
                return VssTransactions.Start(context, new[] { destination }, () => this != null && context.Valid(),
                    locked => CommitCooked(context, selected, destination.GetInventory()), null);
            }
            return false;
        }
        foreach (var output in VssCollectOutput.For(_view.GetZDO().m_uid))
        {
            if (output == null || !output.Eligible(this)) continue;
            var destination = Destination(VssStorageScanner.Sources(context), output.Item.m_itemData);
            if (destination == null) continue;
            return VssTransactions.Start(context, new[] { destination }, () => this != null && output != null && context.Valid() && output.Eligible(this),
                locked => output.Commit(this, context, destination.GetInventory()), null);
        }
        return false;
    }
    internal static Container Destination(List<Container> sources, ItemDrop.ItemData item) => sources
        .Where(c => VssInventoryOps.Capacity(c.GetInventory(), item) > 0)
        .OrderBy(c => VssInventoryOps.Capacity(c.GetInventory(), item, true) > 0 ? 0 : 1)
        .ThenBy(c => VssPreferences.Rank(c, item)).FirstOrDefault();
}

// Only native output emitted by an opted-in machine is tagged. No radius sweep of loose loot.
public sealed class VssCollectOutput : MonoBehaviour
{
    internal const string SourceKey = "vss_output_machine";
    private static readonly Dictionary<ZDOID, HashSet<VssCollectOutput>> Outputs = new();
    internal static VssRefillBridge Spawning;
    internal ItemDrop Item;
    private ZNetView _view;
    private ZDOID _source;
    private static readonly Action<ItemDrop> Load = AccessTools.MethodDelegate<Action<ItemDrop>>(AccessTools.Method(typeof(ItemDrop), "Load"));
    private static readonly Action<ItemDrop> Save = AccessTools.MethodDelegate<Action<ItemDrop>>(AccessTools.Method(typeof(ItemDrop), "Save"));
    private void Awake()
    {
        Item = GetComponent<ItemDrop>(); _view = GetComponent<ZNetView>();
        if (_view == null || !_view.IsValid()) return;
        _source = _view.GetZDO().GetZDOID(SourceKey);
        if (_source == ZDOID.None) return;
        if (!Outputs.TryGetValue(_source, out var items)) Outputs[_source] = items = new HashSet<VssCollectOutput>();
        items.Add(this);
    }
    private void OnDestroy()
    {
        if (!Outputs.TryGetValue(_source, out var items)) return;
        items.Remove(this); if (items.Count == 0) Outputs.Remove(_source);
    }
    internal static VssCollectOutput[] For(ZDOID id) => Outputs.TryGetValue(id, out var items) ? items.ToArray() : Array.Empty<VssCollectOutput>();
    internal static bool Tagged(ItemDrop item) => item != null && item.GetComponent<ZNetView>() is ZNetView view &&
        view.IsValid() && view.GetZDO().GetZDOID(SourceKey) != ZDOID.None;
    internal static ItemDrop StackCandidate(ItemDrop item) => Tagged(item) ? null : item;
    internal static void Track(ItemDrop item)
    {
        if (Tagged(item) && item.GetComponent<VssCollectOutput>() == null) item.gameObject.AddComponent<VssCollectOutput>();
    }
    internal bool Eligible(VssRefillBridge machine)
    {
        if (Item == null || !gameObject.activeInHierarchy || _view == null || !_view.IsValid() ||
            _view.GetZDO().GetZDOID(SourceKey) != _source ||
            (transform.position - machine.transform.position).sqrMagnitude > 100f) return false;
        if (!_view.IsOwner()) { Item.RequestOwn(); return false; }
        Load(Item);
        return Item.m_itemData.m_stack > 0;
    }
    internal bool Commit(VssRefillBridge machine, VssContext context, Inventory destination)
    {
        if (!context.Valid() || !Eligible(machine)) return false;
        var source = new Inventory("VSS machine output", null, 1, 1); source.GetAllItems().Add(Item.m_itemData);
        var moved = VssInventoryOps.Move(source, destination, Item.m_itemData, Item.m_itemData.m_stack);
        if (moved <= 0) return false;
        if (Item.m_itemData.m_stack <= 0) _view.Destroy();
        else { Save(Item); VssGame.ForceSend(ZDOMan.instance, _view.GetZDO().m_uid); }
        return true;
    }
}

[HarmonyPatch(typeof(Smelter), "Spawn")]
internal static class VssSmelterOutputPatch
{
    private static void Prefix(Smelter __instance, out VssRefillBridge __state)
    {
        __state = VssCollectOutput.Spawning;
        VssCollectOutput.Spawning = VssRefillBridge.From(__instance);
    }
    private static Exception Finalizer(Exception __exception, VssRefillBridge __state)
    { VssCollectOutput.Spawning = __state; return __exception; }
}
[HarmonyPatch(typeof(ItemDrop), "OnCreateNew", new[] { typeof(ItemDrop), typeof(bool) })]
internal static class VssOutputCreatedPatch
{
    private static void Postfix(ItemDrop item)
    {
        var machine = VssCollectOutput.Spawning;
        var source = machine != null ? machine.GetComponent<ZNetView>() : null;
        var view = item != null ? item.GetComponent<ZNetView>() : null;
        if (machine == null || !machine.AutoCollect || source == null || !source.IsValid() || !source.IsOwner() ||
            view == null || !view.IsValid() || !view.IsOwner()) return;
        view.GetZDO().Set(VssCollectOutput.SourceKey, source.GetZDO().m_uid);
        ItemDrop.SaveToZDO(item.m_itemData, view.GetZDO());
        VssCollectOutput.Track(item);
    }
}
[HarmonyPatch(typeof(ItemDrop), "Awake")]
internal static class VssOutputLoadedPatch
{
    private static void Postfix(ItemDrop __instance) => VssCollectOutput.Track(__instance);
}
[HarmonyPatch(typeof(ItemDrop), "Load")]
internal static class VssOutputRefreshedPatch
{
    private static void Postfix(ItemDrop __instance) => VssCollectOutput.Track(__instance);
}
[HarmonyPatch(typeof(ItemDrop), "SlowUpdate")]
internal static class VssOutputOwnershipPatch
{
    private static void Prefix(ItemDrop __instance) => VssCollectOutput.Track(__instance);
}
[HarmonyPatch(typeof(ItemDrop), "AutoStackItems")]
internal static class VssOutputStackPatch
{
    private static bool Prefix(ItemDrop __instance) => !VssCollectOutput.Tagged(__instance);
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            yield return instruction;
            // Prevent ordinary loose stacks from absorbing a tagged machine output as well.
            if (instruction.operand is MethodInfo method && method.Name == "GetComponent" && method.ReturnType == typeof(ItemDrop))
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VssCollectOutput), nameof(VssCollectOutput.StackCandidate)));
        }
    }
}
