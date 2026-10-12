using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VSS;

internal enum VssAccessMode { Grid, Resources, Station, Automatic }

internal sealed class VssContext
{
    internal VssAccessStand Stand;
    internal Player Player;
    internal long Actor;
    internal ZNetView Target;
    internal VssAccessMode Mode;
    internal Vector3 Origin => Target != null ? Target.transform.position : Player != null ? Player.transform.position : Stand.transform.position;
    internal ZDOID StandId => Stand.GetComponent<ZNetView>().GetZDO().m_uid;
    internal ZDOID TargetId => Target != null && Target.IsValid() ? Target.GetZDO().m_uid : ZDOID.None;
    internal bool Valid()
    {
        if (!VssSettings.Ready || Stand == null || !Stand.gameObject.activeInHierarchy ||
            Stand.GetComponent<ZNetView>() == null || !Stand.GetComponent<ZNetView>().IsValid()) return false;
        if (Mode == VssAccessMode.Automatic && (Target == null || !Target.IsValid() ||
            !VssRefillBridge.AutoEnabled(Stand.GetComponent<ZNetView>().GetZDO(), Target.GetZDO()))) return false;
        if (Target != null && (!Target.IsValid() || !Target.IsOwner() && (Mode == VssAccessMode.Station || Mode == VssAccessMode.Automatic))) return false;
        if ((Origin - Stand.transform.position).sqrMagnitude > VssSettings.Radius * VssSettings.Radius) return false;
        if (Mode != VssAccessMode.Automatic && (Player == null || Player.IsDead() || Player.IsTeleporting() ||
            Target != null && (Player.transform.position - Origin).sqrMagnitude > 25f)) return false;
        return VssAccessStand.HasWardAccess(Actor, Stand.transform.position) && VssAccessStand.HasWardAccess(Actor, Origin);
    }
    internal static VssContext ForPlayer(Player player, bool useCraftingStation = true)
    {
        if (player == null || player != Player.m_localPlayer || !VssSettings.Ready || player.IsDead() || player.IsTeleporting()) return null;
        var station = useCraftingStation ? player.GetCurrentCraftingStation() : null;
        var target = station != null && (station.transform.position - player.transform.position).sqrMagnitude <= 25f ? station.GetComponent<ZNetView>() : null;
        if (target != null && !target.IsValid()) target = null;
        var origin = target != null ? target.transform.position : player.transform.position;
        var stand = VssAccessStand.Find(origin, player.GetPlayerID());
        return stand == null ? null : new VssContext { Stand = stand, Player = player, Actor = player.GetPlayerID(), Target = target, Mode = VssAccessMode.Resources };
    }
    internal static bool Authorize(long sender, long actor, ZDO stand, ZDO target, VssAccessMode mode)
    {
        if (!VssSettings.Ready || stand == null || stand.GetPrefab() != VssPieceRegistry.PrefabName.GetStableHashCode() || !Enum.IsDefined(typeof(VssAccessMode), mode)) return false;
        var player = Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetPlayerID() == actor && !p.IsDead() && !p.IsTeleporting());
        var playerView = player != null ? player.GetComponent<ZNetView>() : null;
        Vector3 origin;
        if (mode == VssAccessMode.Automatic)
        {
            if (target == null || target.GetOwner() != sender || !VssRefillBridge.AutoEnabled(stand, target) ||
                stand.GetLong(ZDOVars.s_creator, 0L) != actor || !VssRefillBridge.SupportedPrefab(target.GetPrefab())) return false;
            origin = target.GetPosition();
        }
        else
        {
            if (playerView == null || !playerView.IsValid()) return false;
            if (mode == VssAccessMode.Station)
            {
                if (target == null || target.GetOwner() != sender || !VssRefillBridge.SupportedPrefab(target.GetPrefab()) ||
                    (player.transform.position - target.GetPosition()).sqrMagnitude > 25f) return false;
                origin = target.GetPosition();
            }
            else
            {
                if (playerView.GetZDO().GetOwner() != sender) return false;
                origin = mode == VssAccessMode.Grid ? stand.GetPosition() : player.transform.position;
                if (mode == VssAccessMode.Grid && (player.transform.position - origin).sqrMagnitude > 25f) return false;
                if (mode == VssAccessMode.Resources && target != null)
                {
                    var prefab = ZNetScene.instance?.GetPrefab(target.GetPrefab());
                    if (prefab == null || prefab.GetComponent<CraftingStation>() == null || (player.transform.position - target.GetPosition()).sqrMagnitude > 25f) return false;
                    origin = target.GetPosition();
                }
            }
        }
        return (origin - stand.GetPosition()).sqrMagnitude <= VssSettings.Radius * VssSettings.Radius &&
            VssAccessStand.HasWardAccess(actor, origin) && VssAccessStand.HasWardAccess(actor, stand.GetPosition());
    }
}

internal static class VssTransactions
{
    internal static bool Busy { get; private set; }
    internal static int Generation { get; private set; }
    internal static bool Start(VssContext context, IEnumerable<Container> sources, Func<bool> guard,
        Func<List<Container>, bool> action, Action<string> complete)
    {
        if (VssTransfer.Busy || context == null || !context.Valid()) return false;
        var chests = sources.Where(c => c != null).Distinct().OrderBy(c => VssGame.View(c).GetZDO().m_uid.ToString(), StringComparer.Ordinal).ToList();
        Busy = true;
        VssPlugin.Instance.StartCoroutine(Run(context, chests, guard, action, complete));
        return true;
    }
    private static IEnumerator Run(VssContext context, List<Container> chests, Func<bool> guard, Func<List<Container>, bool> action, Action<string> complete)
    {
        var leases = new List<KeyValuePair<VssChestBridge, string>>();
        var error = "Storage changed or is busy. Try again.";
        var valid = true;
        try
        {
            foreach (var chest in chests)
            {
                if (!context.Valid() || !guard() || !VssStorageScanner.CanAccess(chest, context.Actor)) { valid = false; break; }
                var bridge = chest.GetComponent<VssChestBridge>();
                if (bridge == null) { valid = false; error = "A chest owner is missing this VSS version."; break; }
                var token = bridge.Begin(context);
                leases.Add(new KeyValuePair<VssChestBridge, string>(bridge, token));
                var deadline = Time.realtimeSinceStartup + 3f;
                while (bridge != null && !bridge.Rejected && !(bridge.Granted && bridge.CanCommit(token)) && Time.realtimeSinceStartup < deadline)
                {
                    if (!context.Valid() || !guard()) { valid = false; break; }
                    yield return null;
                }
                if (!valid || bridge == null || bridge.Rejected || !bridge.Granted || !bridge.CanCommit(token)) { valid = false; break; }
                VssGame.Load(chest);
            }
            if (valid && context.Valid() && guard() && leases.All(l => l.Key != null && l.Key.CanCommit(l.Value)) &&
                chests.All(c => VssStorageScanner.CanAccess(c, context.Actor)))
            {
                // No yields between final validation, native action, payment and the chest saves.
                try
                {
                    if (action(chests)) error = "";
                }
                catch (Exception ex) { VssPlugin.Log.LogError(ex); error = "Action failed; see BepInEx/LogOutput.log."; }
                finally { foreach (var chest in chests) if (chest != null && VssGame.View(chest).IsOwner()) VssGame.Save(chest); }
            }
        }
        finally
        {
            foreach (var lease in leases) if (lease.Key != null) lease.Key.Release(lease.Value);
            Busy = false; Generation++;
            complete?.Invoke(error);
            VssUi.RefreshNow();
        }
    }
}
