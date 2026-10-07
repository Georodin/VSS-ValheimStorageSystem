using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VSS;

// The current chest owner grants a short lease before handing ownership over.
// All vanilla and VSS writers honor that lease. No player item is removed while waiting for the network.
public sealed class VssChestBridge : MonoBehaviour
{
    internal const string TokenKey = "vss_lease_token";
    internal const string UntilKey = "vss_lease_until";
    private Container _chest;
    private ZNetView _view;
    private string _pendingToken;
    private long _expectedOwner;
    internal bool Rejected { get; private set; }
    internal bool Granted { get; private set; }
    internal static long Now => ZNet.instance != null ? ZNet.instance.GetTime().Ticks : 0;
    internal static bool IsLeased(ZDO zdo) => zdo != null && VssLeaseRules.Active(zdo.GetLong(UntilKey, 0), Now);
    private void Awake()
    {
        _chest = GetComponent<Container>();
        _view = _chest != null ? VssGame.View(_chest) : null;
        if (_view == null || !_view.IsValid()) { enabled = false; return; }
        _view.Register<long, ZDOID, string>("VSS_Acquire", Request);
        _view.Register<string, bool>("VSS_Grant", Reply);
    }
    internal string Begin(Player player, VssAccessStand stand)
    {
        Granted = false; Rejected = false;
        _pendingToken = Guid.NewGuid().ToString("N");
        _expectedOwner = _view.GetZDO().GetOwner();
        var token = _pendingToken;
        _view.InvokeRPC("VSS_Acquire", player.GetPlayerID(), stand.GetComponent<ZNetView>().GetZDO().m_uid, token);
        return token;
    }
    private void Request(long sender, long playerId, ZDOID standId, string token)
    {
        if (!_view.IsOwner()) return;
        var zdo = _view.GetZDO();
        if (!VssSettings.Ready || string.IsNullOrEmpty(token) || token.Length > 64 || _chest.IsInUse() || IsLeased(zdo) ||
            !VssStorageScanner.Supported(_chest) || !VssGame.CheckAccess(_chest, playerId)) { Deny(sender, token); return; }
        var stand = ZDOMan.instance.GetZDO(standId);
        var player = Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetPlayerID() == playerId &&
            p.GetComponent<ZNetView>() != null && p.GetComponent<ZNetView>().IsValid() && p.GetComponent<ZNetView>().GetZDO().GetOwner() == sender);
        if (stand == null || stand.GetPrefab() != VssPieceRegistry.PrefabName.GetStableHashCode() || player == null || player.IsDead() ||
            (stand.GetPosition() - transform.position).sqrMagnitude > VssSettings.Radius * VssSettings.Radius ||
            (stand.GetPosition() - player.transform.position).sqrMagnitude > 25f ||
            (_chest.m_checkGuardStone && !VssAccessStand.HasWardAccess(playerId, transform.position)) ||
            !VssAccessStand.HasWardAccess(playerId, stand.GetPosition())) { Deny(sender, token); return; }
        // Load on the old owner before publishing the lease and fresh items in one ZDO update.
        VssGame.Load(_chest);
        zdo.Set(TokenKey, token);
        zdo.Set(UntilKey, Now + TimeSpan.FromSeconds(10).Ticks);
        zdo.SetOwner(sender);
        ZDOMan.instance.ForceSendZDO(sender, zdo.m_uid);
        _view.InvokeRPC(sender, "VSS_Grant", token, true);
    }
    private void Deny(long sender, string token) => _view.InvokeRPC(sender, "VSS_Grant", token ?? "", false);
    private void Reply(long sender, string token, bool accepted)
    {
        if (token != _pendingToken || sender != _expectedOwner) return;
        Granted = accepted; Rejected = !accepted;
    }
    internal bool CanCommit(string token) => _view != null && _view.IsValid() &&
        VssLeaseRules.CanCommit(_view.IsOwner(), _view.GetZDO().GetString(TokenKey, ""), token, _view.GetZDO().GetLong(UntilKey, 0), Now);
    internal void Release(string token)
    {
        if (_pendingToken == token) _pendingToken = null;
        if (_view == null || !_view.IsValid() || !_view.IsOwner() || _view.GetZDO().GetString(TokenKey, "") != token) return;
        _chest.SetInUse(false);
        _view.GetZDO().Set(UntilKey, 0L);
        _view.GetZDO().Set(TokenKey, "");
        VssGame.ForceSend(ZDOMan.instance, _view.GetZDO().m_uid);
    }
}

[HarmonyPatch(typeof(Container), "Awake")]
internal static class VssContainerAwakePatch
{
    private static void Postfix(Container __instance)
    {
        if (!VssStorageScanner.Supported(__instance, false)) return;
        VssStorageScanner.Containers.Add(__instance);
        if (__instance.GetComponent<VssChestBridge>() == null) __instance.gameObject.AddComponent<VssChestBridge>();
    }
}

[HarmonyPatch]
internal static class VssVanillaChestLeasePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Container), "RPC_RequestOpen");
        yield return AccessTools.Method(typeof(Container), "RPC_RequestStack");
        yield return AccessTools.Method(typeof(Container), "RPC_RequestTakeAll");
    }
    private static bool Prefix(Container __instance, long __0, MethodBase __originalMethod)
    {
        var view = VssGame.View(__instance);
        if (view == null || !view.IsValid() || !view.IsOwner() || !VssChestBridge.IsLeased(view.GetZDO())) return true;
        var reply = __originalMethod.Name == "RPC_RequestOpen" ? "RPC_OpenResponse" :
            __originalMethod.Name == "RPC_RequestStack" ? "RPC_StackResponse" : "RPC_TakeAllResponse";
        view.InvokeRPC(__0, reply, false);
        return false;
    }
}
