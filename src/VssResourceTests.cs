using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VSS;

// Runs only inside the isolated -vss-self-test process, against the game's patched methods.
internal static class VssResourceTests
{
    private static Inventory Inv(int width = 8) => new("VSS isolated resource test", null, width, 1);
    private static ItemDrop.ItemData Item(int amount, string name = "$vss_test_resource", int quality = 1) => new()
    {
        m_stack = amount, m_quality = quality, m_worldLevel = (byte)Game.m_worldLevel,
        m_shared = new ItemDrop.ItemData.SharedData { m_name = name, m_maxStackSize = 50, m_maxQuality = 3, m_weight = 1f }
    };
    private static ItemDrop.ItemData Put(Inventory inventory, ItemDrop.ItemData item)
    { item.m_gridPos = new Vector2i(inventory.NrOfItems(), 0); inventory.GetAllItems().Add(item); return item; }
    internal static void Run(Action<bool, string> check, Player player)
    {
        var oldPlayer = Player.m_localPlayer; var oldPlan = VssResources.Current; var oldReading = VssResources.Reading;
        var oldInventory = player.GetInventory();
        try
        {
            Player.m_localPlayer = player;
            Payments(check);
            PatchedRecipes(check, player);
            Consumption(check, player);
            foreach (var count in new[] { 1, 2, 3, 49, 50, 999 })
            {
                check(VssUi.SelectedAmount(count, 0) == count && VssUi.SelectedAmount(count, 1) == (count + 1) / 2 &&
                    VssUi.SelectedAmount(count, 2) == 1, "stack, rounded half and single selection: " + count);
            }
            check(VssUi.SelectedAmount(0, 2) == 0, "empty row cannot produce a single item");
            check(VssRefillBridge.FuelRoom(0, 10, false) == 10 && VssRefillBridge.FuelRoom(10, 10, false) == 0 &&
                VssRefillBridge.FuelRoom(12, 10, false) == 0, "station fuel capacity prevents overflow");
            check(VssRefillBridge.FuelRoom(9.1f, 10, false) == 0 && VssRefillBridge.FuelRoom(8.1f, 10, false) == 1 &&
                VssRefillBridge.FuelRoom(8.1f, 10, true) == 1, "fractional fuel does not permit overfilling");
            check(VssSettings.AutoExcluded("FineWood") && VssSettings.AutoExcluded("RoundLog") && !VssSettings.AutoExcluded("Wood"),
                "automatic refilling protects special woods by default");
        }
        finally
        {
            Player.m_localPlayer = oldPlayer; VssResources.Current = oldPlan; VssResources.Reading = oldReading;
            AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(player, oldInventory);
        }
    }
    private static void Payments(Action<bool, string> check)
    {
        var own = Inv(); var chest = Inv(); var other = Inv();
        var ownItem = Put(own, Item(3)); var stored = Put(chest, Item(40)); Put(other, Item(40));
        stored.m_customData["owner metadata"] = "retained";
        var plan = VssMaterialPlan.Build(own, new[] { chest, other }, new[] { new VssNeed(stored.m_shared.m_name, 40) });
        check(plan != null && plan.Validate() && plan.Debits[0].Item == ownItem && plan.Debits[0].Amount == 3 &&
            plan.Debits[1].Item == stored && plan.Debits[1].Amount == 37 && plan.ChestInventories.Single() == chest,
            "resource plan uses player resources first and locks only needed chests");
        check(ownItem.m_stack == 3 && stored.m_stack == 40, "planning does not remove items");
        plan.Pay();
        check(plan.Paid && !own.ContainsItem(ownItem) && stored.m_stack == 3 && other.GetAllItems().Single().m_stack == 40 &&
            stored.m_customData["owner metadata"] == "retained", "payment removes exact cost while preserving remaining metadata");
        var rejected = false; try { plan.Pay(); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && stored.m_stack == 3, "same plan cannot pay twice");
        plan = VssMaterialPlan.Build(own, new[] { chest }, new[] { new VssNeed(stored.m_shared.m_name, 2), new VssNeed("$missing", 1) });
        check(plan == null && stored.m_stack == 3, "missing second ingredient leaves every source untouched");
        plan = VssMaterialPlan.Build(own, new[] { chest }, new[] { new VssNeed(stored.m_shared.m_name, 2), new VssNeed(stored.m_shared.m_name, 2) });
        check(plan == null && stored.m_stack == 3, "duplicate recipe costs cannot allocate the same units twice");
        plan = VssMaterialPlan.Build(own, new[] { chest }, new[] { new VssNeed(stored.m_shared.m_name, 2) });
        stored.m_stack = 1;
        check(!plan.Validate(), "changed stack invalidates a pending payment"); stored.m_stack = 3;
        stored.m_customData["owner metadata"] = "changed";
        check(!plan.Validate(), "changed item identity invalidates a pending payment");
        stored.m_customData["owner metadata"] = "retained"; stored.m_equipped = true;
        check(VssMaterialPlan.Build(own, new[] { chest }, new[] { new VssNeed(stored.m_shared.m_name, 1) }) == null,
            "equipped items are never crafting or building materials"); stored.m_equipped = false;
        check(VssMaterialPlan.Build(own, new[] { chest }, new[] { new VssNeed(stored.m_shared.m_name, 1) }, stored) == null,
            "upgrade target is excluded from its own ingredients");
        check(VssMaterialPlan.Build(own, new[] { chest }, new[] { new VssNeed(stored.m_shared.m_name, 1, 2) }) == null,
            "quality-specific payment rejects a different quality");
        var random = new System.Random(30017);
        for (var n = 0; n < 200; n++)
        {
            var inventories = new[] { Inv(), Inv(), Inv() };
            foreach (var inv in inventories) { Put(inv, Item(random.Next(1, 51))); Put(inv, Item(random.Next(1, 51), "$other")); }
            var before = inventories.Sum(i => i.CountItems("$vss_test_resource"));
            var unchanged = inventories.Sum(i => i.CountItems("$other")); var need = random.Next(1, 155);
            plan = VssMaterialPlan.Build(inventories[0], inventories.Skip(1), new[] { new VssNeed("$vss_test_resource", need) });
            check((plan != null) == (before >= need), "random resource plan accurately detects a shortfall");
            if (plan != null) plan.Pay();
            check(inventories.Sum(i => i.CountItems("$vss_test_resource")) == before - (plan == null ? 0 : need) &&
                inventories.Sum(i => i.CountItems("$other")) == unchanged, "random payment conserves exact cost and unrelated items");
        }
    }
    private static void PatchedRecipes(Action<bool, string> check, Player player)
    {
        var root = new GameObject("VSS test recipe ingredient"); root.SetActive(false);
        var drop = root.AddComponent<ItemDrop>(); drop.m_itemData = Item(1);
        var own = Inv(); var chest = Inv(); Put(own, Item(1)); Put(chest, Item(6));
        AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(player, own);
        var recipe = ScriptableObject.CreateInstance<Recipe>();
        recipe.m_resources = new[] { new Piece.Requirement { m_resItem = drop, m_amount = 4 } };
        var have = AccessTools.MethodDelegate<Func<Player, Recipe, bool, int, int, bool>>(AccessTools.Method(typeof(Player), "HaveRequirementItems"));
        VssResources.Current = VssMaterialPlan.ForRecipe(player, recipe, 1, 1, new List<Inventory> { chest }, null);
        check(VssResources.Current != null && own.CountItems(drop.m_itemData.m_shared.m_name) == 1,
            "ordinary inventory counts do not include storage");
        using (new VssResources.Scope(player))
        {
            check(own.CountItems(drop.m_itemData.m_shared.m_name) == 7 && own.HaveItem(drop.m_itemData.m_shared.m_name),
                "native requirement scope includes locked storage");
            using (new VssResources.Scope(null)) check(own.CountItems(drop.m_itemData.m_shared.m_name) == 1, "nested scope can suspend storage counts");
            check(own.CountItems(drop.m_itemData.m_shared.m_name) == 7, "nested requirement scope restores previous player");
        }
        check(have(player, recipe, false, 1, 1) && !have(player, recipe, true, 1, 1),
            "native crafting gate accepts storage while undiscovered recipes stay undiscovered");
        check(VssResources.Reading == null, "native requirement finalizer restores the reading scope");
        player.ConsumeResources(recipe.m_resources, 1);
        check(VssResources.Current.Paid && own.NrOfItems() == 0 && chest.CountItems(drop.m_itemData.m_shared.m_name) == 3,
            "native crafting/building resource consumption pays the locked plan");
        player.ConsumeResources(recipe.m_resources, 1);
        check(chest.CountItems(drop.m_itemData.m_shared.m_name) == 3, "repeated native consumption cannot double charge");
        own = Inv(); chest = Inv(); Put(own, Item(1)); Put(chest, Item(6, quality: 2));
        AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(player, own);
        recipe.m_requireOnlyOneIngredient = true; recipe.m_amount = 3; recipe.m_qualityResultAmountMultiplier = 0.5f;
        recipe.m_resources[0].m_amount = 2; recipe.m_resources[0].m_extraAmountOnlyOneIngredient = 2;
        VssResources.Current = VssMaterialPlan.ForRecipe(player, recipe, 1, 2, new List<Inventory> { chest }, null);
        check(VssResources.Current?.Single?.Quality == 2 && VssResources.Current.Single.Amount == 4,
            "alternate ingredient recipe selects one sufficient quality for the whole batch");
        var resultAmount = recipe.GetAmount(1, out var cost, out var ingredient, 2);
        check(ingredient != null && ingredient.m_quality == 2 && cost == 4 && resultAmount == 14,
            "native alternate recipe receives stored ingredient, batch cost and quality-based output");
        own.RemoveItem(ingredient.m_shared.m_name, cost, ingredient.m_quality);
        check(VssResources.Current.Paid && own.CountItems(ingredient.m_shared.m_name) == 1 && chest.CountItems(ingredient.m_shared.m_name, 2) == 2,
            "native alternate ingredient payment uses storage without consuming another quality");
        var after = player.GetFirstRequiredItem(own, recipe, 1, out _, out _, 2);
        check(after == null, "a paid plan cannot supply a stale ingredient sample for another batch");
        VssResources.Current = null;
        UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(recipe);
    }
    private static void Consumption(Action<bool, string> check, Player player)
    {
        var own = Inv(1); Put(own, Item(50, "$full_inventory")); var chest = Inv();
        AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(player, own);
        var food = Put(chest, Item(2)); food.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
        check(player.ConsumeItem(chest, food, true) && food.m_stack == 1 && own.NrOfItems() == 1,
            "native consumable can be consumed directly from storage with a full player inventory");
        check(player.ConsumeItem(chest, food, true) && !chest.ContainsItem(food), "last stored consumable clears its chest slot");
        var material = Put(chest, Item(2)); material.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
        check(!player.ConsumeItem(chest, material, true) && material.m_stack == 2, "native consumption rejects a non-consumable without removing it");
    }
}
