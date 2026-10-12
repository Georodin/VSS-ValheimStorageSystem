using System;
using System.Collections.Generic;
using HarmonyLib;

namespace VSS;
internal static class VssGame
{
    internal static readonly AccessTools.FieldRef<Container, ZNetView> View = AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
    internal static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
    internal static readonly Action<Container> Save = AccessTools.MethodDelegate<Action<Container>>(AccessTools.Method(typeof(Container), "Save"));
    internal static readonly Func<Container, bool> Load = AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));
    internal static readonly Func<Container, long, bool> CheckAccess = AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
    internal static readonly Action<Inventory, bool, bool> Changed = AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(AccessTools.Method(typeof(Inventory), "Changed"));
    internal static readonly Func<PrivateArea, bool> WardEnabled = AccessTools.MethodDelegate<Func<PrivateArea, bool>>(AccessTools.Method(typeof(PrivateArea), "IsEnabled"));
    internal static readonly Func<PrivateArea, UnityEngine.Vector3, float, bool> WardInside = AccessTools.MethodDelegate<Func<PrivateArea, UnityEngine.Vector3, float, bool>>(AccessTools.Method(typeof(PrivateArea), "IsInside"));
    internal static readonly Func<PrivateArea, long, bool> WardPermitted = AccessTools.MethodDelegate<Func<PrivateArea, long, bool>>(AccessTools.Method(typeof(PrivateArea), "IsPermitted"));
    internal static readonly List<PrivateArea> Wards = AccessTools.StaticFieldRefAccess<List<PrivateArea>>(typeof(PrivateArea), "m_allAreas");
    internal static readonly Action<ZDOMan, ZDOID> ForceSend = AccessTools.MethodDelegate<Action<ZDOMan, ZDOID>>(AccessTools.Method(typeof(ZDOMan), "ForceSendZDO", new[] { typeof(ZDOID) }));
    public static void Notify(Inventory inventory) => Changed(inventory, true, false);
}
