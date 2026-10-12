using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VSS;

internal static class VssRefillTests
{
    internal static void Run(Action<bool, string> check, ZNetScene vanilla)
    {
        // Entire native network stack is temporary: no real world, sockets, peers or saves are opened.
        var root = new GameObject("VSS isolated native refill fixtures"); root.SetActive(false);
        var netField = AccessTools.Field(typeof(ZNet), "m_instance"); var oldNet = netField.GetValue(null);
        var serverField = AccessTools.Field(typeof(ZNet), "m_isServer"); var oldServer = serverField.GetValue(null);
        var routedField = AccessTools.Field(typeof(ZRoutedRpc), "s_instance"); var oldRouted = routedField.GetValue(null);
        var managerField = AccessTools.Field(typeof(ZDOMan), "s_instance"); var oldManager = managerField.GetValue(null);
        var sceneField = AccessTools.Field(typeof(ZNetScene), "s_instance"); var oldScene = sceneField.GetValue(null);
        var readyField = AccessTools.Field(typeof(VssSettings), "<Ready>k__BackingField"); var oldReady = readyField.GetValue(null);
        var oldPlayer = Player.m_localPlayer;
        var oldInit = ZNetView.m_initZDO;
        GameObject controllerRoot = null;
        Player actor = null;
        try
        {
            netField.SetValue(null, root.AddComponent<ZNet>()); serverField.SetValue(null, true);
            var routed = new ZRoutedRpc(true); var manager = new ZDOMan(512); routed.SetUID(ZDOMan.GetSessionID());
            var scene = root.AddComponent<ZNetScene>(); sceneField.SetValue(null, scene);
            var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);
            foreach (var prefab in vanilla.m_prefabs) named[prefab.name.GetStableHashCode()] = prefab;
            readyField.SetValue(null, true);
            var controllerZdo = manager.CreateNewZDO(Vector3.zero, VssPieceRegistry.PrefabName.GetStableHashCode());
            controllerZdo.SetPrefab(VssPieceRegistry.PrefabName.GetStableHashCode());
            controllerRoot = new GameObject("VSS isolated refill controller"); controllerRoot.SetActive(false);
            var controllerView = controllerRoot.AddComponent<ZNetView>();
            AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(controllerView, controllerZdo);
            var controller = controllerRoot.AddComponent<VssAccessStand>();
            ZNetView.m_initZDO = controllerZdo; controllerRoot.SetActive(true);
            VssAccessStand.Instances.Add(controller);
            check(!controller.AutoRefill, "controller master starts Off on native ZDO");
            var actorRoot = new GameObject("VSS isolated refill actor"); actorRoot.transform.SetParent(root.transform);
            var actorView = actorRoot.AddComponent<ZNetView>(); var actorZdo = manager.CreateNewZDO(Vector3.zero, 22222);
            actorZdo.SetPrefab(22222); AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(actorView, actorZdo);
            actor = actorRoot.AddComponent<Player>(); AccessTools.Field(typeof(Character), "m_nview").SetValue(actor, actorView);
            actor.SetPlayerID(9876543L, "VSS isolated actor"); Player.GetAllPlayers().Add(actor); Player.m_localPlayer = actor;
            controller.ToggleAutoRefill(actor);
            check(controller.AutoRefill, "authenticated native controller RPC turns on master");
            foreach (var name in new[] { "charcoal_kiln", "smelter", "blastfurnace", "windmill", "piece_spinningwheel", "piece_groundtorch", "piece_walltorch", "piece_oven" })
                check(VssRefillBridge.SupportedPrefab(name.GetStableHashCode()), "installed native refill component is supported: " + name);
            var fuelRoot = new GameObject("VssTestFuel"); fuelRoot.transform.SetParent(root.transform);
            var fuel = fuelRoot.AddComponent<ItemDrop>();
            fuel.m_itemData = new ItemDrop.ItemData { m_shared = new ItemDrop.ItemData.SharedData { m_name = "$vss_test_fuel", m_maxStackSize = 50 } };
            var inputRoot = new GameObject("VssTestInput"); inputRoot.transform.SetParent(root.transform);
            var input = inputRoot.AddComponent<ItemDrop>();
            input.m_itemData = new ItemDrop.ItemData { m_shared = new ItemDrop.ItemData.SharedData { m_name = "$vss_test_input", m_maxStackSize = 50 } };
            foreach (var type in new[] { typeof(Smelter), typeof(Fireplace), typeof(CookingStation), typeof(ShieldGenerator) })
            {
                VssPlugin.Log.LogInfo("VSS isolated native refill checks: " + type.Name);
                var stationRoot = new GameObject("VSS native " + type.Name); stationRoot.transform.SetParent(root.transform);
                var view = stationRoot.AddComponent<ZNetView>(); var station = stationRoot.AddComponent(type);
                var zdo = manager.CreateNewZDO(Vector3.zero, 12345); zdo.SetPrefab(12345);
                AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(view, zdo);
                AccessTools.Field(type, "m_nview").SetValue(station, view); scene.AddInstance(zdo, view);
                if (station is Smelter smelter)
                {
                    smelter.m_fuelItem = fuel; smelter.m_maxFuel = 3; smelter.m_maxOre = 3;
                    smelter.m_conversion = new List<Smelter.ItemConversion> { new() { m_from = input, m_to = fuel } };
                }
                if (station is Fireplace fire)
                {
                    fire.m_fuelItem = fuel; fire.m_maxFuel = 3; fire.m_canRefill = true;
                    AccessTools.Field(typeof(Fireplace), "m_checkWaterLevel").SetValue(fire, false);
                }
                if (station is CookingStation cooking)
                {
                    cooking.m_fuelItem = fuel; cooking.m_maxFuel = 3; cooking.m_useFuel = true; cooking.m_requireFire = false;
                    cooking.m_slots = new[] { stationRoot.transform, stationRoot.transform, stationRoot.transform };
                    cooking.m_conversion = new List<CookingStation.ItemConversion> { new() { m_from = input, m_to = fuel } };
                    var visuals = 0; view.Register<int, string>("RPC_SetSlotVisual", (_, slot, item) => visuals++);
                }
                if (station is ShieldGenerator shield)
                {
                    shield.m_defaultFuel = 0; shield.m_maxFuel = 3; shield.m_fuelItems = new List<ItemDrop> { fuel };
                    var set = AccessTools.MethodDelegate<Action<ShieldGenerator, long, float>>(AccessTools.Method(typeof(ShieldGenerator), "RPC_SetFuel"));
                    view.Register<float>("RPC_SetFuel", (sender, amount) => set(shield, sender, amount));
                }
                var bridge = stationRoot.AddComponent<VssRefillBridge>();
                AccessTools.Field(typeof(VssRefillBridge), "_view").SetValue(bridge, view);
                var componentField = type == typeof(Smelter) ? "_smelter" : type == typeof(Fireplace) ? "_fire" : type == typeof(CookingStation) ? "_cooking" : "_shield";
                AccessTools.Field(typeof(VssRefillBridge), componentField).SetValue(bridge, station);
                AccessTools.Method(typeof(VssRefillBridge), "RegisterRpc").Invoke(bridge, null);
                check(!bridge.AutoRefill && !VssRefillBridge.AutoEnabled(controllerZdo, zdo), "refillable object starts Off: " + type.Name);
                check(bridge.ToggleAutoRefill(actor) && bridge.AutoRefill && VssRefillBridge.AutoEnabled(controllerZdo, zdo),
                    "authenticated object toggle enables the native saved flag: " + type.Name);
                var context = new VssContext { Stand = controller, Target = view, Actor = actor.GetPlayerID(), Mode = VssAccessMode.Automatic };
                check(context.Valid(), "automatic transaction is authorized when both switches are On: " + type.Name);
                var state = new ZPackage(); zdo.Serialize(state);
                var replica = manager.CreateNewZDO(Vector3.zero, 12345); replica.Deserialize(new ZPackage(state.GetArray()));
                check(replica.GetBool(VssRefillBridge.AutoKey, false), "object choice survives native ZDO serialization: " + type.Name);
                controller.ToggleAutoRefill(actor);
                check(!controller.AutoRefill && bridge.AutoRefill && !VssRefillBridge.AutoEnabled(controllerZdo, zdo),
                    "master Off pauses an opted-in object without clearing its choice: " + type.Name);
                check(!context.Valid(), "turning master Off invalidates pending automatic transactions: " + type.Name);
                check(bridge.HoverHint(actor).Contains("VSS auto-refill: On") && bridge.HoverHint(actor).Contains("master Off"),
                    "native hover hint explains an enabled object paused by the master: " + type.Name);
                controller.ToggleAutoRefill(actor);
                check(bridge.ToggleAutoRefill(actor) && !bridge.AutoRefill && !VssRefillBridge.AutoEnabled(controllerZdo, zdo),
                    "individual Off pauses supplies while master remains On: " + type.Name);
                check(!context.Valid(), "turning object Off invalidates pending automatic transactions: " + type.Name);
                state = new ZPackage(); zdo.Serialize(state); replica.Deserialize(new ZPackage(state.GetArray()));
                check(!replica.GetBool(VssRefillBridge.AutoKey, true), "object Off choice also survives native ZDO serialization: " + type.Name);
                if (station is Fireplace nativeFire)
                {
                    // Base-game $KEY localization requires the normal input bootstrap, which this
                    // isolated process never starts. Exercise the production decorator on the real component.
                    var hover = new object[] { nativeFire, "Native fireplace text" };
                    AccessTools.Method(typeof(VssObjectAutoHoverPatch), "Postfix").Invoke(null, hover);
                    check(((string)hover[1]).StartsWith("Native fireplace text") && ((string)hover[1]).Contains("VSS auto-refill: Off"),
                        "production native hover decorator preserves base text and adds the object control");
                    VssPerformanceTests.Hover(check, bridge, actor);
                }
                var setAuto = AccessTools.MethodDelegate<Action<VssRefillBridge, long, long, bool, string>>(AccessTools.Method(typeof(VssRefillBridge), "SetAutoRefill"));
                setAuto(bridge, ZDOMan.GetSessionID() + 1, actor.GetPlayerID(), true, "wrong-sender");
                check(!bridge.AutoRefill, "object toggle rejects a sender who does not own the actor: " + type.Name);
                setAuto(bridge, ZDOMan.GetSessionID(), actor.GetPlayerID() + 1, true, "unknown-actor");
                check(!bridge.AutoRefill, "object toggle rejects an unknown actor: " + type.Name);
                actor.transform.position = new Vector3(0, 0, 6);
                setAuto(bridge, ZDOMan.GetSessionID(), actor.GetPlayerID(), true, "out-of-range");
                check(!bridge.AutoRefill, "object toggle rejects an actor outside interaction distance: " + type.Name);
                actor.transform.position = Vector3.zero;
                actorZdo.Set(ZDOVars.s_dead, true); setAuto(bridge, ZDOMan.GetSessionID(), actor.GetPlayerID(), true, "dead-actor");
                check(!bridge.AutoRefill, "object toggle rejects a dead actor: " + type.Name); actorZdo.Set(ZDOVars.s_dead, false);
                var inventory = new Inventory("VSS isolated station supply", null, 2, 1);
                var item = fuel.m_itemData.Clone(); item.m_dropPrefab = fuelRoot; item.m_stack = 10;
                item.m_worldLevel = (byte)Game.m_worldLevel; inventory.GetAllItems().Add(item);
                check(bridge.SupplyOne(inventory, item, VssRefillKind.Fuel, false) && item.m_stack == 9 && zdo.GetFloat(ZDOVars.s_fuel) == 1,
                    "owner-side native fuel acceptance debits exactly one: " + type.Name);
                check(!bridge.SupplyOne(inventory, item, VssRefillKind.Fuel, true) && item.m_stack == 9 && zdo.GetFloat(ZDOVars.s_fuel) == 1,
                    "automatic supplies cannot spend from a disabled object's chest: " + type.Name);
                zdo.Set(VssRefillBridge.AutoKey, true);
                check(bridge.SupplyOne(inventory, item, VssRefillKind.Fuel, true) && item.m_stack == 8 && zdo.GetFloat(ZDOVars.s_fuel) == 2,
                    "opted-in native object accepts automatic fuel exactly once: " + type.Name);
                zdo.Set(VssRefillBridge.AutoKey, false); item.m_stack++;
                zdo.Set(ZDOVars.s_fuel, 3f);
                check(!bridge.SupplyOne(inventory, item, VssRefillKind.Fuel, false) && item.m_stack == 9 && zdo.GetFloat(ZDOVars.s_fuel) == 3,
                    "full native fuel user leaves chest quantity unchanged: " + type.Name);
                zdo.Set(ZDOVars.s_fuel, 2.2f);
                check(!bridge.SupplyOne(inventory, item, VssRefillKind.Fuel, false) && item.m_stack == 9,
                    "fractional native fuel cannot overflow: " + type.Name);
                zdo.Set(ZDOVars.s_fuel, 0f); zdo.SetOwnerInternal(ZDOMan.GetSessionID() + 1);
                check(!bridge.SupplyOne(inventory, item, VssRefillKind.Fuel, false) && item.m_stack == 9 && zdo.GetFloat(ZDOVars.s_fuel) == 0,
                    "non-owner cannot accept or debit station supplies: " + type.Name);
                zdo.SetOwnerInternal(ZDOMan.GetSessionID());
                if (station is Smelter || station is CookingStation)
                {
                    var material = input.m_itemData.Clone(); material.m_dropPrefab = inputRoot; material.m_stack = 10;
                    material.m_worldLevel = (byte)Game.m_worldLevel; material.m_gridPos = new Vector2i(1, 0); inventory.GetAllItems().Add(material);
                    check(!bridge.SupplyOne(inventory, item, VssRefillKind.Input, false) && item.m_stack == 9,
                        "incompatible processing input is not consumed: " + type.Name);
                    if (station is CookingStation)
                        check(!bridge.SupplyOne(inventory, material, VssRefillKind.Input, true) && material.m_stack == 10,
                            "automatic cooking never inserts raw food");
                    for (var n = 0; n < 3; n++)
                        check(bridge.SupplyOne(inventory, material, VssRefillKind.Input, false) && material.m_stack == 9 - n,
                            "native input accepts and debits each distinct queue/slot position: " + type.Name + " " + n);
                    check(!bridge.SupplyOne(inventory, material, VssRefillKind.Input, false) && material.m_stack == 7,
                        "full native processing queue/slots reject without spending an item: " + type.Name);
                }
            }
            SafeWood(check, vanilla, root, manager, scene);
            VssPerformanceTests.Run(check, root, manager, actor);
            controller.ToggleAutoRefill(actor);
            check(!controller.AutoRefill, "master Off can be restored after shared object toggles");
        }
        finally
        {
            if (actor != null) Player.GetAllPlayers().Remove(actor);
            if (controllerRoot != null) UnityEngine.Object.DestroyImmediate(controllerRoot);
            Player.m_localPlayer = oldPlayer; readyField.SetValue(null, oldReady); ZNetView.m_initZDO = oldInit;
            netField.SetValue(null, oldNet); serverField.SetValue(null, oldServer); routedField.SetValue(null, oldRouted);
            managerField.SetValue(null, oldManager); sceneField.SetValue(null, oldScene);
            // Inactive fixtures never run their normal world lifecycle or save to disk.
        }
    }
    private static void SafeWood(Action<bool, string> check, ZNetScene vanilla, GameObject root, ZDOMan manager, ZNetScene scene)
    {
        var kilnPrefab = vanilla.GetPrefab("charcoal_kiln"); var native = kilnPrefab.GetComponent<Smelter>();
        var kilnRoot = new GameObject("VSS isolated normal-wood kiln"); kilnRoot.transform.SetParent(root.transform);
        var kiln = kilnRoot.AddComponent<Smelter>(); kiln.m_maxOre = 10; kiln.m_maxFuel = 0; kiln.m_conversion = native.m_conversion;
        var view = kilnRoot.AddComponent<ZNetView>(); var zdo = manager.CreateNewZDO(Vector3.zero, kilnPrefab.name.GetStableHashCode()); zdo.SetPrefab(kilnPrefab.name.GetStableHashCode());
        AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(view, zdo); AccessTools.Field(typeof(Smelter), "m_nview").SetValue(kiln, view); scene.AddInstance(zdo, view);
        var bridge = kilnRoot.AddComponent<VssRefillBridge>();
        AccessTools.Field(typeof(VssRefillBridge), "_view").SetValue(bridge, view); AccessTools.Field(typeof(VssRefillBridge), "_smelter").SetValue(bridge, kiln);
        zdo.Set(VssRefillBridge.AutoKey, true);
        var serverField = AccessTools.Field(typeof(ZNet), "m_isServer"); var oldServer = serverField.GetValue(null);
        var excludedField = AccessTools.Field(typeof(VssSettings), "_excluded"); var oldExcluded = excludedField.GetValue(null);
        try
        {
            // Verify mandatory protection even when a client's synchronized extra exclusion list is empty.
            serverField.SetValue(null, false); excludedField.SetValue(null, "");
            foreach (var name in new[] { "FineWood", "RoundLog", "ElderBark", "YggdrasilWood", "Blackwood" })
            {
                var prefab = vanilla.GetPrefab(name); var material = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                material.m_dropPrefab = prefab; material.m_stack = 3; material.m_worldLevel = (byte)Game.m_worldLevel;
                var inventory = new Inventory("VSS isolated protected wood", null, 1, 1); inventory.GetAllItems().Add(material);
                check(VssSettings.AutoExcluded(name), "special wood stays protected with an empty exclusion list: " + name);
                check(!bridge.SupplyOne(inventory, material, VssRefillKind.Input, true) && material.m_stack == 3 && zdo.GetInt(ZDOVars.s_queued) == 0,
                    "automatic kiln never consumes special wood: " + name);
                check(!bridge.SupplyOne(inventory, material, VssRefillKind.Input, false) && material.m_stack == 3,
                    "normal-interaction VSS supply also protects special wood: " + name);
            }
            var wood = vanilla.GetPrefab("Wood"); var item = wood.GetComponent<ItemDrop>().m_itemData.Clone(); item.m_dropPrefab = wood;
            item.m_stack = 3; item.m_worldLevel = (byte)Game.m_worldLevel;
            var source = new Inventory("VSS isolated normal wood", null, 1, 1); source.GetAllItems().Add(item);
            check(bridge.SupplyOne(source, item, VssRefillKind.Input, true) && item.m_stack == 2 && zdo.GetInt(ZDOVars.s_queued) == 1 && zdo.GetString("item0") == "Wood",
                "native charcoal kiln accepts and charges only normal Wood automatically");
            check(!VssSettings.AutoExcluded("Coal") && !VssSettings.AutoExcluded("Resin"), "normal wood protection keeps coal and resin available");
        }
        finally { serverField.SetValue(null, oldServer); excludedField.SetValue(null, oldExcluded); }
    }
}
