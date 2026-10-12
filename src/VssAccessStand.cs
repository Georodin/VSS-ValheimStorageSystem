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
    internal static int Searches;
    internal static VssAccessStand Find(Vector3 origin, long actor, bool automatic = false)
    {
        Searches++;
        if (Instances.Count == 0 || !HasWardAccess(actor, origin)) return null;
        var radius2 = VssSettings.Radius * VssSettings.Radius;
        VssAccessStand nearest = null;
        var distance = float.MaxValue;
        foreach (var stand in Instances)
        {
            if (stand == null || !stand.gameObject.activeInHierarchy) continue;
            var d = (stand.transform.position - origin).sqrMagnitude;
            if (d > radius2 || d > distance) continue;
            var view = stand.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || automatic && !stand.AutoRefill || !HasWardAccess(actor, stand.transform.position)) continue;
            if (d == distance && nearest != null && System.StringComparer.Ordinal.Compare(view.GetZDO().m_uid.ToString(),
                nearest.GetComponent<ZNetView>().GetZDO().m_uid.ToString()) >= 0) continue;
            nearest = stand; distance = d;
        }
        return nearest;
    }
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
        // Use the same live registry as vanilla. A scene-wide object search here made
        // every build/recipe requirement and refill hover scan the entire world.
        var areas = VssGame.Wards;
        var inside = false;
        foreach (var area in areas)
        {
            if (area == null || !VssGame.WardEnabled(area) || !VssGame.WardInside(area, point, 0f)) continue;
            inside = true;
            var piece = area.GetComponent<Piece>();
            if ((piece != null && piece.GetCreator() == playerId) || VssGame.WardPermitted(area, playerId)) return true;
        }
        return !inside;
    }
}
