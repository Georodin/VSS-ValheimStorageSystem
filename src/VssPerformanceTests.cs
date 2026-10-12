using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VSS;

// Runs only in -vss-self-test, before any real world or networking starts.
internal static class VssPerformanceTests
{
    private static readonly List<string> Results = new();
    internal static void Hover(Action<bool, string> check, VssRefillBridge bridge, Player actor)
    {
        var first = bridge.HoverHint(actor);
        var searches = VssAccessStand.Searches;
        var timer = Stopwatch.StartNew();
        var same = true;
        for (var n = 0; n < 20000; n++) same &= ReferenceEquals(first, bridge.HoverHint(actor));
        timer.Stop();
        check(same && first.Contains("VSS auto-refill"), "repeated native refill hover reuses its formatted string");
        check(VssAccessStand.Searches - searches <= 1, "20,000 refill hover reads do not repeat controller/ward discovery");
        Results.Add($"Refill hover: 20,000 reads in {timer.Elapsed.TotalMilliseconds:F2} ms; controller searches: {VssAccessStand.Searches - searches}.");
    }
    internal static void Run(Action<bool, string> check, GameObject inactiveRoot, ZDOMan manager, Player actor)
    {
        Totals(check);
        var chests = new List<Container>();
        var oldInventory = actor.GetInventory();
        var oldPlan = VssResources.Current;
        var oldReading = VssResources.Reading;
        var oldStation = actor.GetCurrentCraftingStation();
        var oldUseStation = VssResources.UseCraftingStation;
        Recipe recipe = null;
        PrivateArea ward = null;
        try
        {
            var wood = ZNetScene.instance.GetPrefab("Wood".GetStableHashCode());
            var drop = wood.GetComponent<ItemDrop>();
            var name = drop.m_itemData.m_shared.m_name;
            for (var n = 0; n < 60; n++)
            {
                var chestRoot = new GameObject("piece_chest_vss_performance"); chestRoot.SetActive(false);
                chestRoot.transform.position = new Vector3(1 + n % 10, 0, 1 + n / 10);
                var view = chestRoot.AddComponent<ZNetView>();
                var zdo = manager.CreateNewZDO(chestRoot.transform.position, 55555); zdo.SetPrefab(55555);
                AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(view, zdo);
                var chest = chestRoot.AddComponent<Container>(); chest.m_width = 10; chest.m_height = 5; chest.m_checkGuardStone = true;
                chests.Add(chest); ZNetView.m_initZDO = zdo; chestRoot.SetActive(true);
                var inventory = chest.GetInventory();
                for (var slot = 0; slot < 50; slot++)
                {
                    var item = drop.m_itemData.Clone(); item.m_dropPrefab = wood; item.m_quality = 1;
                    item.m_worldLevel = (byte)Game.m_worldLevel; item.m_stack = slot + 1; item.m_gridPos = new Vector2i(slot % 10, slot / 10);
                    inventory.GetAllItems().Add(item);
                }
                VssGame.Notify(inventory);
            }
            var wardRoot = new GameObject("VSS isolated permission fixture"); wardRoot.transform.SetParent(inactiveRoot.transform);
            var wardView = wardRoot.AddComponent<ZNetView>(); var wardZdo = manager.CreateNewZDO(Vector3.zero, 66666);
            AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(wardView, wardZdo);
            ward = wardRoot.AddComponent<PrivateArea>(); ward.m_radius = 100;
            AccessTools.Field(typeof(PrivateArea), "m_nview").SetValue(ward, wardView);
            wardZdo.Set(ZDOVars.s_enabled, true); wardZdo.Set(ZDOVars.s_permitted, 1); wardZdo.Set("pu_id0", actor.GetPlayerID());
            VssGame.Wards.Add(ward);
            check(ReferenceEquals(VssGame.Wards, AccessTools.Field(typeof(PrivateArea), "m_allAreas").GetValue(null)),
                "permissions use vanilla's live ward registry, not a scene search or copied list");
            check(VssAccessStand.HasWardAccess(actor.GetPlayerID(), Vector3.zero) && !VssAccessStand.HasWardAccess(actor.GetPlayerID() + 1, Vector3.zero),
                "native registry permission checks distinguish permitted and denied actors");
            wardZdo.Set(ZDOVars.s_permitted, 0);
            check(!VssAccessStand.HasWardAccess(actor.GetPlayerID(), Vector3.zero), "revoking native ward permission takes effect immediately");
            wardZdo.Set(ZDOVars.s_enabled, false);
            check(VssAccessStand.HasWardAccess(actor.GetPlayerID(), Vector3.zero), "disabled native wards immediately allow access");
            wardZdo.Set(ZDOVars.s_enabled, true); wardZdo.Set(ZDOVars.s_permitted, 1);
            AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(actor, new Inventory("VSS performance actor", null, 8, 4));
            VssResources.Current = null;
            VssResources.Invalidate();
            using (new VssResources.Scope(actor, false))
            {
                var cold = Stopwatch.StartNew();
                check(VssResources.Stored(actor, name, 1, true) == 76500, "building availability discovers all 60 real native chest inventories");
                cold.Stop();
                Results.Add($"Cold availability refresh: {cold.Elapsed.TotalMilliseconds:F2} ms for 60 chests / 3,000 stacks.");
                var refreshes = VssResources.Refreshes; var searches = VssAccessStand.Searches;
                var timer = Stopwatch.StartNew(); var sum = 0;
                for (var n = 0; n < 20000; n++) sum += actor.GetInventory().CountItems(name, 1);
                timer.Stop();
                check(sum == 1530000000 && VssResources.Refreshes == refreshes && VssAccessStand.Searches == searches,
                    "20,000 building HUD reads retain exact totals without rescanning chests or controllers");
                Results.Add($"Building availability: 20,000 patched inventory reads, 60 chests / 3,000 stacks, {timer.Elapsed.TotalMilliseconds:F2} ms; additional refreshes/searches: 0.");
                var firstInventory = chests[0].GetInventory(); firstInventory.RemoveOneItem(firstInventory.GetAllItems()[0]);
                check(VssResources.Stored(actor, name, 1, true) == 76499 && VssResources.Refreshes == refreshes + 1,
                    "native inventory change immediately invalidates indexed availability");
                var previous = VssResources.Refreshes;
                actor.transform.position = new Vector3(1000, 0, 0);
                check(VssResources.Stored(actor, name, 1, true) == 0, "leaving the controller radius immediately removes cached building materials");
                for (var n = 0; n < 20000; n++) VssResources.Stored(actor, name, 1, true);
                check(VssResources.Refreshes == previous + 1, "out-of-range reads cache the absence of a controller too");
                actor.transform.position = Vector3.zero;
                check(VssResources.Stored(actor, name, 1, true) == 76499, "returning to range rebuilds availability");
                var blocked = VssGame.View(chests[0]).GetZDO(); blocked.Set(VssChestBridge.UntilKey, VssChestBridge.Now + TimeSpan.FromSeconds(10).Ticks);
                AccessTools.Field(typeof(VssResources), "_until").SetValue(null, -1f);
                check(VssResources.Stored(actor, name, 1, true) == 75225, "periodic cache refresh excludes a newly leased chest");
                blocked.Set(VssChestBridge.UntilKey, 0L); VssResources.Invalidate();
            }
            var stationRoot = new GameObject("VSS isolated station outside controller range"); stationRoot.transform.SetParent(inactiveRoot.transform);
            stationRoot.transform.position = new Vector3(21, 0, 0);
            var stationView = stationRoot.AddComponent<ZNetView>();
            var stationZdo = manager.CreateNewZDO(stationRoot.transform.position, 77777);
            AccessTools.Field(typeof(ZNetView), "m_zdo").SetValue(stationView, stationZdo);
            var station = stationRoot.AddComponent<CraftingStation>();
            AccessTools.Field(typeof(Player), "m_currentStation").SetValue(actor, station);
            actor.transform.position = new Vector3(18, 0, 0);
            using (new VssResources.Scope(actor, true))
                check(VssResources.Stored(actor, name, 1, true) == 0, "crafting availability uses the station's controller range");
            using (new VssResources.Scope(actor, false))
                check(VssResources.Stored(actor, name, 1, true) == 76499, "building availability uses the player even when a nearby station is outside range");
            actor.transform.position = Vector3.zero; AccessTools.Field(typeof(Player), "m_currentStation").SetValue(actor, null);
            var actorZdo = actor.GetComponent<ZNetView>().GetZDO(); actorZdo.Set(ZDOVars.s_dead, true);
            check(VssResources.Stored(actor, name, 1, true) == 0, "dead players cannot read stale cached resource availability");
            actorZdo.Set(ZDOVars.s_dead, false);
            recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.m_resources = Enumerable.Range(0, 4).Select(_ => new Piece.Requirement { m_resItem = drop, m_amount = 2 }).ToArray();
            var have = AccessTools.MethodDelegate<Func<Player, Recipe, bool, int, int, bool>>(AccessTools.Method(typeof(Player), "HaveRequirementItems"));
            check(have(actor, recipe, false, 1, 1), "native recipe availability includes indexed chest resources");
            var recipeRefreshes = VssResources.Refreshes; var recipeSearches = VssAccessStand.Searches;
            var recipeTimer = Stopwatch.StartNew(); var accepted = 0;
            for (var n = 0; n < 5000; n++) if (have(actor, recipe, false, 1, 1)) accepted++;
            recipeTimer.Stop();
            check(accepted == 5000 && recipeRefreshes == VssResources.Refreshes && recipeSearches == VssAccessStand.Searches,
                "5,000 native four-resource recipe checks share availability without repeated discovery");
            var inventories = chests.Select(c => c.GetInventory()).ToList();
            var linear = Stopwatch.StartNew(); var linearSum = 0;
            for (var n = 0; n < 20000; n++) linearSum += Linear(inventories, name, 1, true);
            linear.Stop();
            check(linearSum == 1529980000, "linear reference and indexed native recipe totals agree");
            Results.Add($"Workbench: 5,000 native four-resource recipe checks ({20000:N0} counts), {recipeTimer.Elapsed.TotalMilliseconds:F2} ms; additional refreshes/searches: 0.");
            Results.Add($"Previous per-query inventory traversal alone: {linear.Elapsed.TotalMilliseconds:F2} ms for 20,000 counts. This excludes the removed Unity scene searches.");
            Results.Add($"Measured recipe checks versus inventory traversal alone: {linear.Elapsed.TotalMilliseconds / Math.Max(0.001, recipeTimer.Elapsed.TotalMilliseconds):F1}x faster.");
            recipe.m_requireOnlyOneIngredient = true; recipe.m_amount = 1;
            recipe.m_resources = new[] { recipe.m_resources[0] };
            check(recipe.GetAmount(1, out var cost, out var selected) == 1 && cost == 2 && selected != null && selected.m_quality == 1,
                "alternate ingredient UI selects a valid stored quality using the availability index");
            var alternateRefreshes = VssResources.Refreshes; var alternateSearches = VssAccessStand.Searches;
            var alternateTimer = Stopwatch.StartNew(); var alternateAccepted = 0;
            for (var n = 0; n < 5000; n++) if (recipe.GetAmount(1, out var needed, out var material) == 1 && needed == 2 && material != null) alternateAccepted++;
            alternateTimer.Stop();
            check(alternateAccepted == 5000 && alternateRefreshes == VssResources.Refreshes && alternateSearches == VssAccessStand.Searches,
                "5,000 native alternate-ingredient previews retain quality and amount without rediscovery");
            Results.Add($"Alternate-ingredient workbench previews: 5,000 native recipe calls, {alternateTimer.Elapsed.TotalMilliseconds:F2} ms; additional refreshes/searches: 0.");
            check(VssResources.Reading == null, "typed native recipe finalizers restore the resource scope after stress checks");
            wardZdo.Set(ZDOVars.s_permitted, 0);
            var denied = VssContext.ForPlayer(actor, false);
            check(denied == null || !denied.Valid(), "authoritative actions bypass availability caches after ward permission revocation");
            WriteReport();
        }
        finally
        {
            if (ward != null) VssGame.Wards.Remove(ward);
            foreach (var chest in chests) { VssStorageScanner.Containers.Remove(chest); if (chest != null) UnityEngine.Object.DestroyImmediate(chest.gameObject); }
            if (recipe != null) UnityEngine.Object.DestroyImmediate(recipe);
            actor.transform.position = Vector3.zero;
            AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(actor, oldInventory);
            AccessTools.Field(typeof(Player), "m_currentStation").SetValue(actor, oldStation);
            VssResources.Current = oldPlan; VssResources.Reading = oldReading; VssResources.UseCraftingStation = oldUseStation;
            VssResources.Invalidate();
        }
    }
    private static int Linear(IEnumerable<Inventory> inventories, string name, int quality, bool world)
    {
        var count = 0;
        foreach (var inventory in inventories) foreach (var item in inventory.GetAllItems())
            if (item.m_stack > 0 && !item.m_equipped && (name == null || item.m_shared.m_name == name) &&
                (quality < 0 || item.m_quality == quality) && (!world || item.m_worldLevel >= Game.m_worldLevel)) count = checked(count + item.m_stack);
        return count;
    }
    private static void Totals(Action<bool, string> check)
    {
        var random = new System.Random(30232); var inventory = new Inventory("VSS totals reference", null, 50, 10);
        var totals = new VssResourceTotals(); totals.Reset(2);
        for (var n = 0; n < 500; n++)
        {
            var item = new ItemDrop.ItemData { m_stack = random.Next(0, 51), m_quality = random.Next(0, 4), m_worldLevel = (byte)random.Next(0, 5),
                m_equipped = n % 11 == 0, m_shared = new ItemDrop.ItemData.SharedData { m_name = "$resource" + n % 10 } };
            inventory.GetAllItems().Add(item); totals.Add(item);
        }
        foreach (var name in Enumerable.Range(0, 12).Select(n => "$resource" + n).Concat(new string[] { null }))
            foreach (var q in new[] { -3, -1, 0, 1, 2, 3, 4 }) foreach (var world in new[] { false, true })
            {
                var expected = inventory.GetAllItems().Where(i => i.m_stack > 0 && !i.m_equipped && (name == null || i.m_shared.m_name == name) &&
                    (q < 0 || i.m_quality == q) && (!world || i.m_worldLevel >= 2)).Sum(i => i.m_stack);
                check(totals.Count(name, q, world) == expected, "availability index preserves native name, quality, world and equipment filtering");
            }
        totals.Reset(0);
        check(totals.Count(null, -1, false) == 0, "resetting availability cannot retain another network's resources");
    }
    private static void WriteReport()
    {
        var text = string.Join(Environment.NewLine, Results);
        VssPlugin.Log.LogInfo("VSS performance diagnostics:" + Environment.NewLine + text);
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-vss-test-report");
        if (index >= 0 && index + 1 < args.Length) File.WriteAllText(Path.ChangeExtension(args[index + 1], "performance.txt"), text);
    }
}
