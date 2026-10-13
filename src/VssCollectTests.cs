using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace VSS;

internal static class VssCollectTests
{
    internal static void Run(Action<bool, string> check, ZNetScene vanilla, GameObject root, ZDOMan manager,
        ZNetScene scene, Player actor, VssAccessStand controller)
    {
        check(VssConfig.MigrateRefillKey(new KeyboardShortcut(KeyCode.G)).Equals(new KeyboardShortcut(KeyCode.F6)), "legacy G default migrates to F6");
        check(VssConfig.MigrateRefillKey(new KeyboardShortcut(KeyCode.F8)).Equals(new KeyboardShortcut(KeyCode.F8)), "custom refill key remains intact");
        check(VssConfig.MigrateRefillKey(new KeyboardShortcut(KeyCode.G, KeyCode.LeftControl)).Equals(new KeyboardShortcut(KeyCode.G, KeyCode.LeftControl)), "custom modified G shortcut remains intact");
        var controllerZdo = controller.GetComponent<ZNetView>().GetZDO();
        controllerZdo.Set(ZDOVars.s_creator, actor.GetPlayerID());
        check(!controller.AutoCollect, "collection master starts Off independently of refill");
        controller.ToggleAutoCollect(actor); check(controller.AutoCollect && controller.AutoRefill, "native collection master RPC does not alter refill master");
        foreach (var name in new[] { "charcoal_kiln", "smelter", "blastfurnace", "windmill", "piece_spinningwheel", "piece_cookingstation", "piece_oven" })
            check(VssRefillBridge.CollectSupportedPrefab(name.GetStableHashCode()), "native collection machine supported: " + name);
        check(!VssRefillBridge.CollectSupportedPrefab("piece_groundtorch".GetStableHashCode()), "lamps offer only refill, not collection");
        var nativeGrill = vanilla.GetPrefab("piece_cookingstation").GetComponent<CookingStation>();
        var grillRoot = new GameObject("VSS isolated collector grill"); grillRoot.transform.SetParent(root.transform);
        var grillView = grillRoot.AddComponent<ZNetView>(); var grill = grillRoot.AddComponent<CookingStation>();
        var grillZdo = manager.CreateNewZDO(Vector3.zero, "piece_cookingstation".GetStableHashCode()); grillZdo.SetPrefab("piece_cookingstation".GetStableHashCode());
        AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(grillView, grillZdo);
        AccessTools.Field(typeof(CookingStation), "m_nview").SetValue(grill, grillView);
        scene.AddInstance(grillZdo, grillView);
        grill.m_conversion = nativeGrill.m_conversion; grill.m_overCookedItem = nativeGrill.m_overCookedItem;
        grill.m_slots = new[] { grillRoot.transform, grillRoot.transform }; grill.m_useFuel = false;
        var bridge = grillRoot.AddComponent<VssRefillBridge>();
        AccessTools.Field(typeof(VssRefillBridge), "_view").SetValue(bridge, grillView);
        AccessTools.Field(typeof(VssRefillBridge), "_cooking").SetValue(bridge, grill);
        AccessTools.Method(typeof(VssRefillBridge), "RegisterRpc").Invoke(bridge, null);
        var visuals = 0; grillView.Register<int, string>("RPC_SetSlotVisual", (_, slot, item) => visuals++);
        check(!bridge.AutoCollect && bridge.CanAutoCollect && !bridge.CanAutoRefill, "wood grill supports independent collection without an oven fuel control");
        check(bridge.ToggleAutoCollect(actor) && bridge.AutoCollect && VssRefillBridge.CollectEnabled(controllerZdo, grillZdo), "native authenticated object collection toggle works");
        var context = new VssContext { Stand = controller, Actor = actor.GetPlayerID(), Target = grillView, Mode = VssAccessMode.AutomaticCollect };
        check(context.Valid() && VssContext.Authorize(ZDOMan.GetSessionID(), actor.GetPlayerID(), controllerZdo, grillZdo, VssAccessMode.AutomaticCollect), "owner-side collection authorization uses separate saved switches");
        check(!VssContext.Authorize(ZDOMan.GetSessionID() + 1, actor.GetPlayerID(), controllerZdo, grillZdo, VssAccessMode.AutomaticCollect), "remote non-owner cannot collect output");
        var setCollect = AccessTools.MethodDelegate<Action<VssRefillBridge, long, long, bool, string>>(AccessTools.Method(typeof(VssRefillBridge), "SetAutoCollect"));
        setCollect(bridge, ZDOMan.GetSessionID() + 1, actor.GetPlayerID(), false, "spoofed"); check(bridge.AutoCollect, "spoofed toggle cannot turn off collection");
        actor.transform.position = new Vector3(0, 0, 6); setCollect(bridge, ZDOMan.GetSessionID(), actor.GetPlayerID(), false, "too-far");
        check(bridge.AutoCollect, "collection toggle requires interaction distance"); actor.transform.position = Vector3.zero;
        var snapshot = new ZPackage(); grillZdo.Serialize(snapshot); var replica = manager.CreateNewZDO(Vector3.zero, 88888);
        replica.Deserialize(new ZPackage(snapshot.GetArray())); check(replica.GetBool(VssRefillBridge.CollectKey, false), "collection switch survives native save replication");
        var cooked = grill.m_conversion[0].m_to; var raw = grill.m_conversion[0].m_from;
        var itemPrefabs = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ObjectDB), "m_itemByHash").GetValue(ObjectDB.instance);
        itemPrefabs[cooked.name.GetStableHashCode()] = cooked.gameObject;
        itemPrefabs[grill.m_overCookedItem.name.GetStableHashCode()] = grill.m_overCookedItem.gameObject;
        grillZdo.Set("slot0", raw.name); grillZdo.Set("slotstatus0", 0);
        var destination = new Inventory("VSS cooked-output destination", null, 1, 1);
        check(!bridge.CommitCooked(context, 0, destination) && destination.NrOfItems() == 0, "uncooked food cannot be collected");
        grillZdo.Set("slot0", cooked.name); grillZdo.Set("slotstatus0", 1); grillZdo.Set(ZDOVars.s_cheatedQueued, true);
        var sample = bridge.CookedOutput(0, out _, out _, out _); sample.m_stack = sample.m_shared.m_maxStackSize - 1;
        destination.GetAllItems().Add(sample);
        check(bridge.CommitCooked(context, 0, destination) && sample.m_stack == sample.m_shared.m_maxStackSize &&
            grillZdo.GetString("slot0") == "" && visuals == 1, "finished food stacks exactly once before native slot and visuals clear");
        check(sample.m_cheated && sample.m_worldLevel == Game.m_worldLevel, "cooked output preserves native cheat and world flags");
        check(!bridge.CommitCooked(context, 0, destination), "cleared cooking slot cannot duplicate food");
        grillZdo.Set("slot0", cooked.name); grillZdo.Set("slotstatus0", 1);
        check(!bridge.CommitCooked(context, 0, destination) && grillZdo.GetString("slot0") == cooked.name, "full storage leaves finished food on the grill");
        controller.ToggleAutoCollect(actor);
        check(!context.Valid() && !bridge.CommitCooked(context, 0, new Inventory("empty", null, 1, 1)) && bridge.AutoCollect,
            "master Off cancels pending collection while retaining object choice");
        check(bridge.HoverHint(actor).Contains("F7") && bridge.HoverHint(actor).Contains("VSS auto-collect: On") && bridge.HoverHint(actor).Contains("master Off"), "grill hover explains separate F7 collection master");
        controller.ToggleAutoCollect(actor);
        var outputs = new[] { cooked, grill.m_overCookedItem };
        foreach (var output in outputs)
        {
            grillZdo.Set("slot0", output.name); grillZdo.Set("slotstatus0", output == cooked ? 1 : 2);
            var target = new Inventory("VSS cooked or already burnt output", null, 1, 1);
            check(bridge.CommitCooked(context, 0, target) && target.NrOfItems() == 1 && target.GetAllItems()[0].m_shared.m_name == output.m_itemData.m_shared.m_name,
                "native cooked/burnt output identity is preserved: " + output.name);
        }
        LeasedCooking(check, manager, grillZdo, bridge, context, cooked);
        OutputDrop(check, root, manager, scene, vanilla, controller, actor);
        controller.ToggleAutoCollect(actor); check(!controller.AutoCollect && controller.AutoRefill, "collection master restores Off without changing refill");
    }
    private static void LeasedCooking(Action<bool, string> check, ZDOMan manager, ZDO grillZdo,
        VssRefillBridge bridge, VssContext context, ItemDrop cooked)
    {
        var chests = new List<Container>();
        try
        {
            for (var n = 0; n < 2; n++)
            {
                var chestRoot = new GameObject("piece_chest_vss_collection_test"); chestRoot.SetActive(false);
                chestRoot.transform.position = new Vector3(n + 1, 0, 0);
                var view = chestRoot.AddComponent<ZNetView>(); var zdo = manager.CreateNewZDO(chestRoot.transform.position, 44444); zdo.SetPrefab(44444);
                AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(view, zdo);
                var chest = chestRoot.AddComponent<Container>(); chest.m_width = 1; chest.m_height = 1; chests.Add(chest);
                ZNetView.m_initZDO = zdo; chestRoot.SetActive(true);
            }
            var sample = cooked.m_itemData.Clone(); sample.m_dropPrefab = cooked.gameObject;
            sample.m_worldLevel = (byte)Game.m_worldLevel; sample.m_stack = sample.m_shared.m_maxStackSize - 1;
            chests[1].GetInventory().GetAllItems().Add(sample); VssGame.Notify(chests[1].GetInventory());
            grillZdo.Set("slot0", cooked.name); grillZdo.Set("slotstatus0", 1);
            check(bridge.RunCollect(context), "collection starts the real exclusive chest lease transaction");
            check(!VssTransactions.Busy && chests[1].GetInventory().CountItems(sample.m_shared.m_name) == sample.m_shared.m_maxStackSize && chests[0].GetInventory().NrOfItems() == 0 && grillZdo.GetString("slot0") == "",
                "leased collection stacks in a farther existing stack before a nearer empty chest");
            check(!VssChestBridge.IsLeased(VssGame.View(chests[1]).GetZDO()), "collection releases its chest lease after commit");
            var saved = new Inventory("VSS saved collection", null, 1, 1);
            saved.Load(new ZPackage(VssGame.View(chests[1]).GetZDO().GetByteArray(ZDOVars.s_items)));
            check(saved.CountItems(sample.m_shared.m_name) == sample.m_shared.m_maxStackSize, "leased collected food persists in native chest save");
            grillZdo.Set("slot0", cooked.name); grillZdo.Set("slotstatus0", 1);
            chests[0].SetInUse(true);
            check(!bridge.RunCollect(context) && grillZdo.GetString("slot0") == cooked.name, "busy and full destinations leave output untouched");
            chests[0].SetInUse(false);
        }
        finally
        {
            foreach (var chest in chests) { VssStorageScanner.Containers.Remove(chest); UnityEngine.Object.DestroyImmediate(chest.gameObject); }
        }
    }
    private static void OutputDrop(Action<bool, string> check, GameObject root, ZDOMan manager, ZNetScene scene,
        ZNetScene vanilla, VssAccessStand controller, Player actor)
    {
        var kilnRoot = new GameObject("VSS isolated collector kiln"); kilnRoot.transform.SetParent(root.transform);
        var view = kilnRoot.AddComponent<ZNetView>(); var kiln = kilnRoot.AddComponent<Smelter>();
        var zdo = manager.CreateNewZDO(Vector3.zero, "charcoal_kiln".GetStableHashCode()); zdo.SetPrefab("charcoal_kiln".GetStableHashCode());
        AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(view, zdo);
        AccessTools.Field(typeof(Smelter), "m_nview").SetValue(kiln, view);
        scene.AddInstance(zdo, view);
        kiln.m_conversion = vanilla.GetPrefab("charcoal_kiln").GetComponent<Smelter>().m_conversion;
        var bridge = kilnRoot.AddComponent<VssRefillBridge>(); AccessTools.Field(typeof(VssRefillBridge), "_view").SetValue(bridge, view);
        AccessTools.Field(typeof(VssRefillBridge), "_smelter").SetValue(bridge, kiln);
        zdo.Set(VssRefillBridge.CollectKey, true);
        var coal = kiln.m_conversion[0].m_to;
        var dropRoot = new GameObject(coal.name); dropRoot.SetActive(false); var dropView = dropRoot.AddComponent<ZNetView>();
        var dropZdo = manager.CreateNewZDO(Vector3.zero, coal.name.GetStableHashCode()); dropZdo.SetPrefab(coal.name.GetStableHashCode());
        AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(dropView, dropZdo);
        // Keep ItemDrop inactive so its world/terrain lifecycle never starts; the output tracker has no world lifecycle.
        dropRoot.SetActive(false); var drop = dropRoot.AddComponent<ItemDrop>();
        drop.m_itemData = coal.m_itemData.Clone(); drop.m_itemData.m_dropPrefab = coal.gameObject; drop.m_itemData.m_stack = 7;
        AccessTools.Field(typeof(ItemDrop), "m_nview").SetValue(drop, dropView);
        VssCollectOutput.Spawning = bridge; ItemDrop.OnCreateNew(drop, true); VssCollectOutput.Spawning = null;
        check(VssCollectOutput.Tagged(drop) && dropZdo.GetZDOID(VssCollectOutput.SourceKey) == zdo.m_uid, "native output creation tags exact source machine");
        check(VssCollectOutput.StackCandidate(drop) == null, "ordinary loose piles cannot absorb tagged output");
        var output = drop.GetComponent<VssCollectOutput>(); AccessTools.Method(typeof(VssCollectOutput), "Awake").Invoke(output, null);
        check(VssCollectOutput.For(zdo.m_uid).Contains(output), "output provenance is indexed by machine without scene searches");
        var ordinaryRoot = new GameObject("ordinary untagged coal"); ordinaryRoot.transform.SetParent(root.transform);
        var ordinary = ordinaryRoot.AddComponent<ItemDrop>(); check(VssCollectOutput.StackCandidate(ordinary) == ordinary, "unrelated loose loot retains vanilla stacking");
        var state = new ZPackage(); dropZdo.Serialize(state); var replica = manager.CreateNewZDO(Vector3.zero, 99999);
        replica.Deserialize(new ZPackage(state.GetArray())); check(replica.GetZDOID(VssCollectOutput.SourceKey) == zdo.m_uid, "output provenance survives native save replication");
        var itemPrefabs = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ObjectDB), "m_itemByHash").GetValue(ObjectDB.instance);
        itemPrefabs[coal.name.GetStableHashCode()] = coal.gameObject;
        // Awake uses the isolated network. The fixture is destroyed before any terrain update runs.
        drop.enabled = false; ZNetView.m_initZDO = dropZdo; dropRoot.SetActive(true);
        scene.AddInstance(dropZdo, dropView);
        try
        {
            var context = new VssContext { Stand = controller, Actor = actor.GetPlayerID(), Target = view, Mode = VssAccessMode.AutomaticCollect };
            var target = new Inventory("VSS output chest", null, 1, 1);
            var partial = drop.m_itemData.Clone(); partial.m_stack = partial.m_shared.m_maxStackSize - 2; target.GetAllItems().Add(partial);
            check(output.Commit(bridge, context, target) && partial.m_stack == partial.m_shared.m_maxStackSize && drop.m_itemData.m_stack == 5,
                "machine output deposits only available capacity and leaves remaining loose stack");
            check(!output.Commit(bridge, context, target) && drop.m_itemData.m_stack == 5, "full chest cannot lose or duplicate remaining output");
            zdo.Set(VssRefillBridge.CollectKey, false);
            check(!output.Commit(bridge, context, new Inventory("empty", null, 1, 1)), "object Off cancels pending output deposit"); zdo.Set(VssRefillBridge.CollectKey, true);
            dropRoot.transform.position = new Vector3(11, 0, 0);
            check(!output.Commit(bridge, context, new Inventory("empty", null, 1, 1)), "output moved away from its machine is not collected"); dropRoot.transform.position = Vector3.zero;
            var empty = new Inventory("VSS final output destination", null, 1, 1);
            check(output.Commit(bridge, context, empty) && empty.CountItems(coal.m_itemData.m_shared.m_name) == 5,
                "remaining output moves exactly once before native world drop is destroyed");
            check(!dropView.IsValid(), "depleted output no longer has an active native network view");
        }
        finally { UnityEngine.Object.DestroyImmediate(dropRoot); }
        kiln.m_outputPoint = kilnRoot.transform;
        var spawn = AccessTools.MethodDelegate<Action<Smelter, string, int>>(AccessTools.Method(typeof(Smelter), "Spawn"));
        spawn(kiln, kiln.m_conversion[0].m_from.name, 3);
        var emitted = VssCollectOutput.For(zdo.m_uid).Single();
        GameObject looseRoot = null;
        try
        {
            check(emitted.Item.m_itemData.m_stack == 3 && emitted.Item.m_itemData.m_cheated == false && VssCollectOutput.Spawning == null,
                "actual native smelter Spawn is captured with exact stack and restored scope");
            looseRoot = UnityEngine.Object.Instantiate(coal.gameObject, Vector3.zero, Quaternion.identity);
            var loose = looseRoot.GetComponent<ItemDrop>(); loose.m_itemData.m_stack = 4;
            ItemDrop.OnCreateNew(loose, false); ItemDrop.SaveToZDO(loose.m_itemData, loose.GetComponent<ZNetView>().GetZDO());
            Physics.SyncTransforms();
            var autoStack = AccessTools.MethodDelegate<Action<ItemDrop>>(AccessTools.Method(typeof(ItemDrop), "AutoStackItems"));
            autoStack(loose); autoStack(emitted.Item);
            check(loose.m_itemData.m_stack == 4 && emitted.Item.m_itemData.m_stack == 3,
                "patched native ground auto-stack keeps machine output separate from unrelated loose loot");
            var target = new Inventory("VSS native produced output", null, 1, 1);
            var context = new VssContext { Stand = controller, Actor = actor.GetPlayerID(), Target = view, Mode = VssAccessMode.AutomaticCollect };
            check(emitted.Commit(bridge, context, target) && target.CountItems(coal.m_itemData.m_shared.m_name) == 3 && loose.m_itemData.m_stack == 4,
                "actual native emitted coal collects exactly once while ordinary loot stays in world");
        }
        finally
        {
            if (emitted != null) UnityEngine.Object.DestroyImmediate(emitted.gameObject);
            if (looseRoot != null) UnityEngine.Object.DestroyImmediate(looseRoot);
        }
    }
}
