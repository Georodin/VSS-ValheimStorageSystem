using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VSS;
public sealed class VssAccessStand : MonoBehaviour, Interactable, Hoverable
{
    internal static readonly HashSet<VssAccessStand> Instances = new();
    internal const string AutoKey = "vss_auto_refill";
    private bool _registered;
    internal bool AutoRefill => GetComponent<ZNetView>() is ZNetView view && view.IsValid() && view.GetZDO().GetBool(AutoKey, false);
    private void Awake()
    {
        var view = GetComponent<ZNetView>();
        Register(view);
    }
    private void OnDestroy() => Instances.Remove(this);
    private void Start()
    {
        var view = GetComponent<ZNetView>();
        Register(view);
    }
    private void Register(ZNetView view)
    {
        if (view == null || !view.IsValid()) return;
        Instances.Add(this);
        if (_registered) return;
        _registered = true;
        view.Register<long, bool>("VSS_SetAutoRefill", SetAutoRefill);
    }
    internal void ToggleAutoRefill(Player player)
    {
        if (player == null || !VssSettings.Ready || (player.transform.position - transform.position).sqrMagnitude > 25f) return;
        GetComponent<ZNetView>().InvokeRPC("VSS_SetAutoRefill", player.GetPlayerID(), !AutoRefill);
    }
    private void SetAutoRefill(long sender, long actor, bool enabled)
    {
        var view = GetComponent<ZNetView>();
        var player = Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetPlayerID() == actor);
        var pv = player != null ? player.GetComponent<ZNetView>() : null;
        if (!view.IsOwner() || !VssSettings.Ready || pv == null || !pv.IsValid() || pv.GetZDO().GetOwner() != sender ||
            player.IsDead() || (player.transform.position - transform.position).sqrMagnitude > 25f || !HasWardAccess(actor, transform.position)) return;
        view.GetZDO().Set(AutoKey, enabled);
        VssGame.ForceSend(ZDOMan.instance, view.GetZDO().m_uid);
    }
    internal static VssAccessStand Find(Vector3 origin, long actor, bool automatic = false) => Instances
        .Where(s => s != null && s.gameObject.activeInHierarchy && s.GetComponent<ZNetView>() is ZNetView view && view.IsValid() &&
            (!automatic || s.AutoRefill) && (s.transform.position - origin).sqrMagnitude <= VssSettings.Radius * VssSettings.Radius &&
            HasWardAccess(actor, s.transform.position) && HasWardAccess(actor, origin))
        .OrderBy(s => (s.transform.position - origin).sqrMagnitude)
        .ThenBy(s => s.GetComponent<ZNetView>().GetZDO().m_uid.ToString(), System.StringComparer.Ordinal).FirstOrDefault();
    public string GetHoverText() => Localization.instance.Localize("Storage Inventory\n[<color=yellow><b>$KEY_Use</b></color>] Open storage");
    public string GetHoverName() => "Storage Inventory";
    public float GetHoverOffset() => 0.75f;
    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold || !(user is Player player) || player != Player.m_localPlayer) return false;
        if (!PrivateArea.CheckAccess(transform.position, 0f, true, false)) return true;
        VssUi.Open(player, this);
        return true;
    }
    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    internal static bool HasWardAccess(long playerId, Vector3 point)
    {
        // PrivateArea.CheckAccess uses the LOCAL player. On an owner peer, check the requesting ID explicitly.
        var areas = UnityEngine.Object.FindObjectsByType<PrivateArea>(FindObjectsSortMode.None);
        var inside = false;
        foreach (var area in areas)
        {
            if (!VssGame.WardEnabled(area) || !VssGame.WardInside(area, point, 0f)) continue;
            inside = true;
            var piece = area.GetComponent<Piece>();
            if ((piece != null && piece.GetCreator() == playerId) || VssGame.WardPermitted(area, playerId)) return true;
        }
        return !inside;
    }
}
