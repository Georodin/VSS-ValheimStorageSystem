using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VSS;
internal static class VssStorageScanner
{
    internal static readonly HashSet<Container> Containers = new();
    public static bool Supported(Container container)
    {
        if (container == null || container.m_wagon != null || container.m_autoDestroyEmpty) return false;
        var view = VssGame.View(container);
        if (view == null || !view.IsValid()) return false;
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(view.GetZDO().GetPrefab()) : null;
        var name = prefab != null ? prefab.name : container.gameObject.name.Replace("(Clone)", "").Trim();
        return name.StartsWith("piece_chest", StringComparison.Ordinal) || name == "piece_barrel";
    }
    public static bool CanAccess(Container container, Player player) => player != null && Supported(container) &&
        VssGame.CheckAccess(container, player.GetPlayerID()) &&
        (!container.m_checkGuardStone || PrivateArea.CheckAccess(container.transform.position, 0f, false, false));
    internal static bool CanAccess(Container container, long actor) => Supported(container) && VssGame.CheckAccess(container, actor) &&
        (!container.m_checkGuardStone || VssAccessStand.HasWardAccess(actor, container.transform.position));
    internal static List<Container> Sources(VssContext context)
    {
        Containers.RemoveWhere(c => c == null);
        var sources = Containers.Where(c => c.gameObject.activeInHierarchy &&
            (c.transform.position - context.Stand.transform.position).sqrMagnitude <= VssSettings.Radius * VssSettings.Radius &&
            CanAccess(c, context.Actor) && !c.IsInUse() && !VssChestBridge.IsLeased(VssGame.View(c).GetZDO()))
            .OrderBy(c => (c.transform.position - context.Stand.transform.position).sqrMagnitude).ToList();
        foreach (var chest in sources) VssGame.Load(chest);
        return sources;
    }
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
