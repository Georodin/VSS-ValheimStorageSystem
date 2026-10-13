using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VSS;

internal enum VssRefillKind { Fuel, Input }

public sealed partial class VssRefillBridge : MonoBehaviour
{
    internal const string AutoKey = "vss_object_auto_refill";
    internal static readonly HashSet<VssRefillBridge> Instances = new();
    private ZNetView _view;
    private Smelter _smelter;
    private Fireplace _fire;
    private CookingStation _cooking;
    private ShieldGenerator _shield;
    private string _request;
    private long _expectedOwner;
    private float _requestUntil, _lastUse;
    private string _autoRequest;
    private long _autoOwner;
    private float _autoUntil;
    private Player _hintPlayer;
    private VssAccessStand _hintStand;
    private Vector3 _hintPosition;
    private float _hintUntil, _hintRadius;
    private bool _hintAuto, _hintMaster, _hintCollect, _hintCollectMaster;
    private BepInEx.Configuration.KeyboardShortcut _hintRefillKey, _hintCollectKey;
    private string _hintText;
    private static float _nextAuto;
    private static int _cursor;
    private static readonly Func<CookingStation, bool> CookingFire = AccessTools.MethodDelegate<Func<CookingStation, bool>>(AccessTools.Method(typeof(CookingStation), "IsFireLit"));
    private static readonly Func<CookingStation, int> FreeSlot = AccessTools.MethodDelegate<Func<CookingStation, int>>(AccessTools.Method(typeof(CookingStation), "GetFreeSlot"));
    private static readonly Func<CookingStation, bool> DoneFood = AccessTools.MethodDelegate<Func<CookingStation, bool>>(AccessTools.Method(typeof(CookingStation), "HaveDoneItem"));
    private static readonly Action<Smelter, long> SmelterFuel = AccessTools.MethodDelegate<Action<Smelter, long>>(AccessTools.Method(typeof(Smelter), "RPC_AddFuel"));
    private static readonly Action<Smelter, long, string, bool> SmelterInput = AccessTools.MethodDelegate<Action<Smelter, long, string, bool>>(AccessTools.Method(typeof(Smelter), "RPC_AddOre"));
    private static readonly Action<Fireplace, long> FireFuel = AccessTools.MethodDelegate<Action<Fireplace, long>>(AccessTools.Method(typeof(Fireplace), "RPC_AddFuel"));
    private static readonly Action<CookingStation, long> CookingFuel = AccessTools.MethodDelegate<Action<CookingStation, long>>(AccessTools.Method(typeof(CookingStation), "RPC_AddFuel"));
    private static readonly Action<CookingStation, long, string, bool> CookingInput = AccessTools.MethodDelegate<Action<CookingStation, long, string, bool>>(AccessTools.Method(typeof(CookingStation), "RPC_AddItem"));
    private static readonly Action<ShieldGenerator, long> ShieldFuel = AccessTools.MethodDelegate<Action<ShieldGenerator, long>>(AccessTools.Method(typeof(ShieldGenerator), "RPC_AddFuel"));
    internal bool AutoRefill => _view != null && _view.IsValid() && _view.GetZDO().GetBool(AutoKey, false);
    internal static bool AutoEnabled(ZDO controller, ZDO target) => controller != null && target != null &&
        controller.GetBool(VssAccessStand.AutoKey, false) && target.GetBool(AutoKey, false);
    internal bool CanAutoRefill =>
        _smelter != null && (_smelter.m_maxFuel > 0 && _smelter.m_fuelItem != null || _smelter.m_maxOre > 0 && _smelter.m_conversion.Count > 0) ||
        _fire != null && _fire.m_canRefill && !_fire.m_infiniteFuel && _fire.m_maxFuel > 0 && _fire.m_fuelItem != null ||
        _cooking != null && _cooking.m_useFuel && _cooking.m_maxFuel > 0 && _cooking.m_fuelItem != null ||
        _shield != null && _shield.m_maxFuel > 0 && _shield.m_fuelItems.Count > 0;
    internal static bool SupportedPrefab(int hash)
    {
        var prefab = ZNetScene.instance?.GetPrefab(hash);
        return prefab != null && (prefab.GetComponentInChildren<Smelter>(true) != null || prefab.GetComponentInChildren<Fireplace>(true) != null ||
            prefab.GetComponentInChildren<CookingStation>(true) != null || prefab.GetComponentInChildren<ShieldGenerator>(true) != null);
    }
    private void Awake()
    {
        _view = GetComponent<ZNetView>();
        if (_view == null || !_view.IsValid()) { enabled = false; return; }
        _smelter = GetComponentInChildren<Smelter>(true); _fire = GetComponentInChildren<Fireplace>(true);
        _cooking = GetComponentInChildren<CookingStation>(true); _shield = GetComponentInChildren<ShieldGenerator>(true);
        RegisterRpc();
        Instances.Add(this);
    }
    private void RegisterRpc()
    {
        _view.Register<ZPackage>("VSS_Refill", Request);
        _view.Register<string, int, string>("VSS_RefillResult", Reply);
        _view.Register<long, bool, string>("VSS_SetObjectAuto", SetAutoRefill);
        _view.Register<string, bool, bool>("VSS_ObjectAutoResult", AutoReply);
        RegisterCollectRpc();
    }
    private bool CanToggle(Player player) => player != null && !player.IsDead() && !player.IsTeleporting() &&
        VssSettings.Ready && _view != null && _view.IsValid() && CanAutoRefill &&
        (player.transform.position - transform.position).sqrMagnitude <= 25f &&
        VssAccessStand.HasWardAccess(player.GetPlayerID(), transform.position) &&
        VssAccessStand.Find(transform.position, player.GetPlayerID()) != null;
    internal bool ToggleAutoRefill(Player player)
    {
        if (player != Player.m_localPlayer || !CanToggle(player)) return false;
        if (_autoRequest != null && Time.unscaledTime < _autoUntil) return true;
        _autoRequest = Guid.NewGuid().ToString("N"); _autoOwner = _view.GetZDO().GetOwner(); _autoUntil = Time.unscaledTime + 3f;
        _view.InvokeRPC("VSS_SetObjectAuto", player.GetPlayerID(), !AutoRefill, _autoRequest);
        return true;
    }
    private void SetAutoRefill(long sender, long actor, bool enabled, string token)
    {
        if (_view == null || !_view.IsValid() || !_view.IsOwner() || string.IsNullOrEmpty(token) || token.Length > 64) return;
        var player = Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetPlayerID() == actor);
        var pv = player != null ? player.GetComponent<ZNetView>() : null;
        var accepted = pv != null && pv.IsValid() && pv.GetZDO().GetOwner() == sender && CanToggle(player);
        if (accepted)
        {
            _view.GetZDO().Set(AutoKey, enabled);
            VssGame.ForceSend(ZDOMan.instance, _view.GetZDO().m_uid);
        }
        _view.InvokeRPC(sender, "VSS_ObjectAutoResult", token, accepted, AutoRefill);
    }
    private void AutoReply(long sender, string token, bool accepted, bool enabled)
    {
        if (sender != _autoOwner || token != _autoRequest) return;
        _autoRequest = null;
        var player = Player.m_localPlayer;
        if (player == null) return;
        var stand = VssAccessStand.Find(transform.position, player.GetPlayerID());
        player.Message(MessageHud.MessageType.TopLeft, !accepted ? "Cannot change VSS auto-refill here." :
            "VSS auto-refill: " + (enabled ? "On" : "Off") + (enabled && (stand == null || !stand.AutoRefill) ? " (controller master Off)." : "."));
    }
    internal static string AutoHint(bool enabled, bool master, string key) =>
        "\n[<color=yellow><b>" + key + "</b></color>] VSS auto-refill: " + (enabled ? "On" : "Off") +
        (enabled && !master ? " <color=#b0a58d>(master Off)</color>" : "");
    internal string HoverHint(Player player)
    {
        if (player == null || player.IsDead() || player.IsTeleporting() || !VssSettings.Ready ||
            _view == null || !_view.IsValid() || !(CanAutoRefill || CanAutoCollect) || (player.transform.position - transform.position).sqrMagnitude > 25f) return "";
        if (_hintPlayer != player || Time.unscaledTime >= _hintUntil || _hintRadius != VssSettings.Radius ||
            (transform.position - _hintPosition).sqrMagnitude > 0.25f)
        {
            _hintPlayer = player; _hintPosition = transform.position; _hintRadius = VssSettings.Radius;
            _hintStand = VssAccessStand.Find(transform.position, player.GetPlayerID());
            _hintUntil = Time.unscaledTime + 0.25f; _hintText = null;
        }
        if (_hintStand == null) return "";
        var auto = AutoRefill; var master = _hintStand.AutoRefill;
        var collect = AutoCollect; var collectMaster = _hintStand.AutoCollect;
        var refillKey = VssPlugin.ModConfig.AutoRefillToggleKey.Value;
        var collectKey = VssPlugin.ModConfig.AutoCollectToggleKey.Value;
        if (_hintText == null || _hintAuto != auto || _hintMaster != master || _hintCollect != collect ||
            _hintCollectMaster != collectMaster || !_hintRefillKey.Equals(refillKey) || !_hintCollectKey.Equals(collectKey))
        {
            _hintAuto = auto; _hintMaster = master; _hintCollect = collect; _hintCollectMaster = collectMaster;
            _hintRefillKey = refillKey; _hintCollectKey = collectKey;
            _hintText = (CanAutoRefill ? AutoHint(auto, master, refillKey.ToString()) : "") +
                (CanAutoCollect ? AutoHint(collect, collectMaster, collectKey.ToString()).Replace("auto-refill", "auto-collect") : "");
        }
        return _hintText;
    }
    private void OnDestroy() => Instances.Remove(this);
    internal static int FuelRoom(float fuel, float maximum, bool ceil) => Math.Max(0, Mathf.FloorToInt(maximum - (ceil ? Mathf.Ceil(fuel) : fuel)));
    internal int Room(VssRefillKind kind)
    {
        if (_view == null || !_view.IsValid()) return 0;
        if (kind == VssRefillKind.Fuel)
        {
            if (_fire != null && (!_fire.m_canRefill || _fire.m_infiniteFuel || _fire.m_fuelItem == null)) return 0;
            if (_smelter != null && _smelter.m_fuelItem == null || _cooking != null && (!_cooking.m_useFuel || _cooking.m_fuelItem == null) ||
                _shield != null && _shield.m_fuelItems.Count == 0) return 0;
            var maximum = _smelter != null ? _smelter.m_maxFuel : _fire != null ? _fire.m_maxFuel : _cooking != null ? _cooking.m_maxFuel : _shield != null ? _shield.m_maxFuel : 0;
            return FuelRoom(_view.GetZDO().GetFloat(ZDOVars.s_fuel, _shield != null ? _shield.m_defaultFuel : 0f), maximum, _fire != null);
        }
        if (_smelter != null) return Math.Max(0, _smelter.m_maxOre - _view.GetZDO().GetInt(ZDOVars.s_queued));
        if (_cooking != null && (!_cooking.m_requireFire || CookingFire(_cooking))) return FreeSlot(_cooking) >= 0 ? 1 : 0;
        return 0;
    }
    internal bool Allows(ItemDrop.ItemData item, VssRefillKind kind, bool automatic)
    {
        if (item == null || item.m_equipped || item.m_stack <= 0 || item.m_worldLevel < Game.m_worldLevel ||
            VssSettings.ProtectedWood(VssItemKey.Prefab(item)) ||
            automatic && VssSettings.AutoExcluded(VssItemKey.Prefab(item))) return false;
        if (kind == VssRefillKind.Fuel)
        {
            var fuel = _smelter?.m_fuelItem ?? _fire?.m_fuelItem ?? _cooking?.m_fuelItem;
            return fuel != null ? fuel.m_itemData.m_shared.m_name == item.m_shared.m_name :
                _shield != null && _shield.m_fuelItems.Any(f => f != null && f.m_itemData.m_shared.m_name == item.m_shared.m_name);
        }
        var prefab = VssItemKey.Prefab(item);
        if (_smelter != null) return _smelter.m_conversion.Any(c => c.m_from != null && c.m_from.name == prefab);
        // Cooking racks/ovens are filled manually; automatic cooking could burn unattended food.
        return !automatic && _cooking != null && _cooking.m_conversion.Any(c => c.m_from != null && c.m_from.name == prefab);
    }
    private int Measure(VssRefillKind kind) => kind == VssRefillKind.Fuel ? Mathf.RoundToInt(_view.GetZDO().GetFloat(ZDOVars.s_fuel) * 10000f) :
        _smelter != null ? _view.GetZDO().GetInt(ZDOVars.s_queued) : _cooking != null ? (FreeSlot(_cooking) < 0 ? _cooking.m_slots.Length : FreeSlot(_cooking)) : 0;
    private bool AddOne(ItemDrop.ItemData item, VssRefillKind kind)
    {
        if (!_view.IsOwner() || Room(kind) <= 0) return false;
        var before = Measure(kind);
        try
        {
            if (kind == VssRefillKind.Input)
            {
                if (_smelter != null) SmelterInput(_smelter, _view.GetZDO().GetOwner(), VssItemKey.Prefab(item), item.m_cheated);
                else if (_cooking != null) CookingInput(_cooking, _view.GetZDO().GetOwner(), VssItemKey.Prefab(item), item.m_cheated);
            }
            else if (_smelter != null) SmelterFuel(_smelter, _view.GetZDO().GetOwner());
            else if (_fire != null) FireFuel(_fire, _view.GetZDO().GetOwner());
            else if (_cooking != null) CookingFuel(_cooking, _view.GetZDO().GetOwner());
            else if (_shield != null) ShieldFuel(_shield, _view.GetZDO().GetOwner());
        }
        catch (Exception ex) { VssPlugin.Log.LogError(ex); }
        // Debit only for an actual native owner-side increase, never for a sent/queued RPC.
        return Measure(kind) > before;
    }
    internal bool SupplyOne(Inventory inventory, ItemDrop.ItemData item, VssRefillKind kind, bool automatic)
    {
        if (inventory == null || !inventory.ContainsItem(item) || automatic && !AutoRefill ||
            !Allows(item, kind, automatic) || !AddOne(item, kind)) return false;
        inventory.RemoveOneItem(item);
        return true;
    }
    internal bool Manual(Player player, VssRefillKind kind)
    {
        if (player == null || player != Player.m_localPlayer || !VssSettings.Ready || _view == null || !_view.IsValid() || Room(kind) <= 0 ||
            player.GetInventory().GetAllItems().Any(i => Allows(i, kind, false))) return false;
        if (_cooking != null && kind == VssRefillKind.Input && DoneFood(_cooking)) return false;
        var stand = VssAccessStand.Find(transform.position, player.GetPlayerID());
        if (stand == null || (player.transform.position - transform.position).sqrMagnitude > 25f) return false;
        if (_request != null && Time.unscaledTime < _requestUntil || Time.unscaledTime - _lastUse < 0.2f) return true;
        var context = new VssContext { Player = player, Actor = player.GetPlayerID(), Stand = stand, Target = _view, Mode = VssAccessMode.Resources };
        if (!VssStorageScanner.Sources(context).Any(c => c.GetInventory().GetAllItems().Any(i => Allows(i, kind, false)))) return false;
        _lastUse = Time.unscaledTime; _request = Guid.NewGuid().ToString("N"); _requestUntil = Time.unscaledTime + 8f;
        _expectedOwner = _view.GetZDO().GetOwner();
        var packet = new ZPackage(); packet.Write(player.GetPlayerID()); packet.Write(stand.GetComponent<ZNetView>().GetZDO().m_uid);
        packet.Write((int)kind); packet.Write(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? Math.Min(50, Room(kind)) : 1); packet.Write(_request);
        _view.InvokeRPC("VSS_Refill", packet);
        return true;
    }
    private void Request(long sender, ZPackage packet)
    {
        if (!_view.IsOwner() || !VssSettings.Ready) return;
        long actor; ZDOID standId; VssRefillKind kind; int amount; string token;
        try { actor = packet.ReadLong(); standId = packet.ReadZDOID(); kind = (VssRefillKind)packet.ReadInt(); amount = packet.ReadInt(); token = packet.ReadString(); }
        catch { return; }
        if (!Enum.IsDefined(typeof(VssRefillKind), kind) || amount < 1 || amount > 50 || string.IsNullOrEmpty(token) || token.Length > 64) return;
        var player = Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetPlayerID() == actor && !p.IsDead() && !p.IsTeleporting());
        var pv = player != null ? player.GetComponent<ZNetView>() : null;
        var stand = VssAccessStand.Instances.FirstOrDefault(s => s != null && s.GetComponent<ZNetView>().IsValid() && s.GetComponent<ZNetView>().GetZDO().m_uid == standId);
        if (pv == null || !pv.IsValid() || pv.GetZDO().GetOwner() != sender || stand == null ||
            (player.transform.position - transform.position).sqrMagnitude > 25f || !VssAccessStand.HasWardAccess(actor, transform.position)) return;
        var context = new VssContext { Stand = stand, Player = player, Actor = actor, Target = _view, Mode = VssAccessMode.Station };
        if (!Run(context, kind, amount, (count, message) =>
        {
            if (_view != null && _view.IsValid()) _view.InvokeRPC(sender, "VSS_RefillResult", token, count, message);
        }))
            _view.InvokeRPC(sender, "VSS_RefillResult", token, 0, "Storage is empty or busy.");
    }
    private void Reply(long sender, string token, int amount, string message)
    {
        if (sender != _expectedOwner || token != _request) return;
        _request = null;
        Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, amount > 0 ? $"Added {amount} from storage." : message);
    }
    private bool Run(VssContext context, VssRefillKind kind, int maximum, Action<int, string> complete)
    {
        if (VssTransfer.Busy || !context.Valid() || Room(kind) <= 0) return false;
        var automatic = context.Mode == VssAccessMode.Automatic;
        var source = VssStorageScanner.Sources(context).FirstOrDefault(c => c.GetInventory().GetAllItems().Any(i => Allows(i, kind, automatic)));
        if (source == null) return false;
        var added = 0;
        return VssTransactions.Start(context, new[] { source }, () => this != null && _view != null && _view.IsValid() && _view.IsOwner(), locked =>
        {
            var inventory = source.GetInventory();
            foreach (var item in inventory.GetAllItems().ToList())
            {
                while (added < maximum && Room(kind) > 0 && inventory.ContainsItem(item) && Allows(item, kind, automatic) &&
                    (!automatic || context.Valid()))
                {
                    if (!SupplyOne(inventory, item, kind, automatic)) break;
                    added++;
                }
                if (added >= maximum || Room(kind) <= 0) break;
            }
            if (added > 0) VssGame.ForceSend(ZDOMan.instance, _view.GetZDO().m_uid);
            return added > 0;
        }, error => complete?.Invoke(added, error));
    }
    internal static void Tick()
    {
        if (!VssSettings.Ready || VssTransfer.Busy || Time.unscaledTime < _nextAuto || ZNetScene.instance == null) return;
        _nextAuto = Time.unscaledTime + 1f;
        Instances.RemoveWhere(s => s == null);
        var targets = Instances.Where(s => s._view != null && s._view.IsValid() && s._view.IsOwner() &&
            s.gameObject.activeInHierarchy && s.AutoRefill && s.CanAutoRefill)
            .OrderBy(s => s._view.GetZDO().m_uid.ToString(), StringComparer.Ordinal).ToList();
        for (var n = 0; n < targets.Count; n++)
        {
            var index = (_cursor + n) % targets.Count; var target = targets[index];
            // Choose the nearest eligible controller before checking its master switch, so an
            // overlapping enabled network cannot bypass the selected network's Off switch.
            var stand = VssAccessStand.FindAutomatic(target.transform.position);
            if (stand == null || !stand.AutoRefill) continue;
            var context = new VssContext { Stand = stand, Actor = stand.GetComponent<Piece>().GetCreator(), Target = target._view, Mode = VssAccessMode.Automatic };
            foreach (var kind in new[] { VssRefillKind.Fuel, VssRefillKind.Input })
            {
                if (target._cooking != null && kind == VssRefillKind.Input) continue;
                if (target.Run(context, kind, 50, null)) { _cursor = (index + 1) % targets.Count; return; }
            }
        }
    }
    internal static VssRefillBridge From(Component station)
    {
        var view = station.GetComponent<ZNetView>() ?? station.GetComponentInParent<ZNetView>();
        return view != null ? view.GetComponent<VssRefillBridge>() : null;
    }
}

[HarmonyPatch(typeof(Player), "UpdateHover")]
internal static class VssObjectAutoInputPatch
{
    private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));
    private static void Postfix(Player __instance)
    {
        if (__instance != Player.m_localPlayer || VssUi.IsOpen || __instance.InPlaceMode() ||
            Hud.InRadial() || !TakeInput(__instance)) return;
        var refill = VssPlugin.ModConfig.AutoRefillToggleKey.Value.IsDown();
        var collect = VssPlugin.ModConfig.AutoCollectToggleKey.Value.IsDown();
        if (!refill && !collect) return;
        var hovered = __instance.GetHoverObject();
        var bridge = hovered != null ? hovered.GetComponentInParent<VssRefillBridge>() : null;
        if (collect) bridge?.ToggleAutoCollect(__instance);
        else if (refill) bridge?.ToggleAutoRefill(__instance);
    }
}

[HarmonyPatch]
internal static class VssObjectAutoHoverPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Switch), "GetHoverText");
        yield return AccessTools.Method(typeof(Fireplace), "GetHoverText");
        yield return AccessTools.Method(typeof(CookingStation), "GetHoverText");
        yield return AccessTools.Method(typeof(ShieldGenerator), "GetHoverText");
    }
    private static void Postfix(Component __instance, ref string __result)
    {
        var player = Player.m_localPlayer;
        if (player == null) return;
        var bridge = VssRefillBridge.From(__instance);
        if (bridge != null) __result += bridge.HoverHint(player);
    }
}

[HarmonyPatch]
internal static class VssRefillAwakePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Smelter), "Awake");
        yield return AccessTools.Method(typeof(Fireplace), "Awake");
        yield return AccessTools.Method(typeof(CookingStation), "Awake");
        yield return AccessTools.Method(typeof(ShieldGenerator), "Start");
    }
    private static void Postfix(Component __instance)
    {
        var view = __instance.GetComponent<ZNetView>() ?? __instance.GetComponentInParent<ZNetView>();
        if (view != null && view.IsValid() && view.GetComponent<VssRefillBridge>() == null) view.gameObject.AddComponent<VssRefillBridge>();
    }
}
[HarmonyPatch]
internal static class VssManualRefillPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Smelter), "OnAddFuel"); yield return AccessTools.Method(typeof(Smelter), "OnAddOre");
        yield return AccessTools.Method(typeof(Fireplace), "Interact");
        yield return AccessTools.Method(typeof(CookingStation), "OnAddFuelSwitch"); yield return AccessTools.Method(typeof(CookingStation), "OnInteract");
        yield return AccessTools.Method(typeof(ShieldGenerator), "OnAddFuel");
    }
    private static bool Prefix(Component __instance, object[] __args, MethodBase __originalMethod, ref bool __result)
    {
        var player = __args.OfType<Player>().FirstOrDefault();
        if (player == null || player != Player.m_localPlayer || __args.OfType<ItemDrop.ItemData>().Any()) return true;
        if (__instance is Fireplace fire)
        {
            var hold = (bool)__args[1]; var alt = (bool)__args[2]; var view = fire.GetComponent<ZNetView>();
            if (hold && fire.m_holdRepeatInterval <= 0f || fire.m_canTurnOff && !hold && !alt && view != null && view.IsValid() && view.GetZDO().GetFloat(ZDOVars.s_fuel) > 0f) return true;
        }
        var kind = __originalMethod.Name == "OnAddOre" || __originalMethod.Name == "OnInteract" ? VssRefillKind.Input : VssRefillKind.Fuel;
        var bridge = VssRefillBridge.From(__instance);
        if (bridge == null || !bridge.Manual(player, kind)) return true;
        __result = true; return false;
    }
}
