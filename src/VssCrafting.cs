using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VSS;

internal static class VssCrafting
{
    private static readonly AccessTools.FieldRef<InventoryGui, Recipe> RecipeField = AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");
    private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> Upgrade = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_craftUpgradeItem");
    private static readonly AccessTools.FieldRef<InventoryGui, bool> Multiple = AccessTools.FieldRefAccess<InventoryGui, bool>("m_multiCrafting");
    private static readonly AccessTools.FieldRef<InventoryGui, int> MultipleAmount = AccessTools.FieldRefAccess<InventoryGui, int>("m_multiCraftAmount");
    private static readonly AccessTools.FieldRef<InventoryGui, int> Variant = AccessTools.FieldRefAccess<InventoryGui, int>("m_craftVariant");
    private static readonly Action<InventoryGui, Player> Craft = AccessTools.MethodDelegate<Action<InventoryGui, Player>>(AccessTools.Method(typeof(InventoryGui), "DoCrafting"));
    internal static int Cancellation;
    internal static bool CanBegin(InventoryGui gui)
    {
        if (!VssTransfer.Busy) return true;
        var player = Player.m_localPlayer; var recipe = RecipeField(gui);
        if (player == null || recipe == null) return false;
        if (player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)) return true;
        var upgrade = Upgrade(gui);
        return VssMaterialPlan.ForRecipe(player, recipe, upgrade == null ? 1 : upgrade.m_quality + 1,
            Multiple(gui) ? MultipleAmount(gui) : 1, new List<Inventory>(), upgrade) != null;
    }
    internal static bool BeforeCraft(InventoryGui gui, Player player)
    {
        if (VssResources.Current != null || player != Player.m_localPlayer || player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)) return true;
        var context = VssContext.ForPlayer(player); var recipe = RecipeField(gui);
        if (context == null || recipe == null) return true;
        var upgrade = Upgrade(gui); var quality = upgrade == null ? 1 : upgrade.m_quality + 1;
        var multiple = Multiple(gui); var amount = multiple ? MultipleAmount(gui) : 1; var variant = Variant(gui); var cancellation = Cancellation;
        var chests = VssStorageScanner.Sources(context);
        var plan = VssMaterialPlan.ForRecipe(player, recipe, quality, amount, chests.Select(c => c.GetInventory()).ToList(), upgrade);
        if (plan == null) { player.Message(MessageHud.MessageType.TopLeft, "Storage resources are missing or busy."); return false; }
        if (!plan.UsesStorage) return true;
        if (VssTransfer.Busy) { player.Message(MessageHud.MessageType.TopLeft, "Storage is busy. Try again."); return false; }
        var station = player.GetCurrentCraftingStation();
        bool Guard() => Cancellation == cancellation && gui != null && InventoryGui.IsVisible() && player.GetCurrentCraftingStation() == station &&
            RecipeField(gui) == recipe && Upgrade(gui) == upgrade && (upgrade == null || upgrade.m_quality + 1 == quality) &&
            Multiple(gui) == multiple && (!multiple || MultipleAmount(gui) == amount) && Variant(gui) == variant;
        VssTransactions.Start(context, chests.Where(c => plan.ChestInventories.Contains(c.GetInventory())), Guard, locked =>
        {
            var payment = VssMaterialPlan.ForRecipe(player, recipe, quality, amount, locked.Select(c => c.GetInventory()).ToList(), upgrade);
            if (payment == null || !payment.Validate()) return false;
            VssResources.Current = payment;
            try { Craft(gui, player); return payment.Paid; }
            finally { VssResources.Current = null; }
        }, error => { if (!string.IsNullOrEmpty(error) && player != null) player.Message(MessageHud.MessageType.TopLeft, error); });
        return false;
    }
}

[HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
internal static class VssCraftActionPatch
{
    private static bool Prefix(InventoryGui __instance, Player player) => VssCrafting.BeforeCraft(__instance, player);
}
[HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
internal static class VssCraftBusyPatch { private static bool Prefix(InventoryGui __instance) => VssCrafting.CanBegin(__instance); }
[HarmonyPatch(typeof(InventoryGui), "OnCraftCancelPressed")]
internal static class VssCraftCancelPatch { private static void Prefix() => VssCrafting.Cancellation++; }
[HarmonyPatch(typeof(InventoryGui), "Hide")]
internal static class VssCraftClosePatch { private static void Prefix() => VssCrafting.Cancellation++; }

[HarmonyPatch]
internal static class VssRequirementsReadPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Player), "HaveRequirementItems");
        yield return AccessTools.Method(typeof(Player), "HaveRequirements", new[] { typeof(Piece), typeof(Player.RequirementMode) });
        yield return AccessTools.Method(typeof(Player), "GetFirstRequiredItem");
    }
    private static void Prefix(Player __instance, object[] __args, MethodBase __originalMethod, out VssResources.Scope __state)
    {
        __state = null;
        if (__instance != Player.m_localPlayer) return;
        if (__originalMethod.Name == "HaveRequirementItems" && (bool)__args[1]) return;
        if (__originalMethod.Name == "HaveRequirements" && (Player.RequirementMode)__args[1] == Player.RequirementMode.IsKnown) return;
        __state = new VssResources.Scope(__instance, __originalMethod.Name != "HaveRequirements");
    }
    private static Exception Finalizer(Exception __exception, VssResources.Scope __state) { __state?.Dispose(); return __exception; }
}
[HarmonyPatch(typeof(InventoryGui), "SetupRequirement")]
internal static class VssRequirementColorPatch
{
    private static void Prefix(Player player, bool craft, out VssResources.Scope __state) => __state = new VssResources.Scope(player == Player.m_localPlayer ? player : null, craft);
    private static Exception Finalizer(Exception __exception, VssResources.Scope __state) { __state?.Dispose(); return __exception; }
}
[HarmonyPatch(typeof(Inventory), "CountItems")]
internal static class VssResourceCountPatch
{
    private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
    {
        var player = VssResources.Reading;
        if (player != null && __instance == player.GetInventory()) __result += VssResources.Stored(player, name, quality, matchWorldLevel);
    }
}
[HarmonyPatch(typeof(Inventory), "HaveItem", new[] { typeof(string), typeof(bool) })]
internal static class VssResourceHavePatch
{
    private static void Postfix(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
    {
        var player = VssResources.Reading;
        if (!__result && player != null && __instance == player.GetInventory()) __result = VssResources.Stored(player, name, -1, matchWorldLevel) > 0;
    }
}
[HarmonyPatch(typeof(Player), "GetFirstRequiredItem")]
internal static class VssSingleIngredientPatch
{
    private static void Postfix(Player __instance, Recipe recipe, int qualityLevel, int craftMultiplier,
        ref int amount, ref int extraAmount, ref ItemDrop.ItemData __result)
    {
        if (__instance != Player.m_localPlayer) return;
        var current = VssResources.Current;
        if (current != null && !current.Paid && current.Player == __instance && current.Requirements == recipe.m_resources && current.Single != null)
        { __result = current.SingleSample; amount = current.Single.Amount; }
        else if (__result == null)
        {
            var plan = VssMaterialPlan.ForRecipe(__instance, recipe, qualityLevel, craftMultiplier, VssResources.Inventories(__instance), null);
            if (plan?.Single != null) { __result = plan.SingleSample; amount = plan.Single.Amount; }
        }
        if (__result != null)
        {
            var sample = __result;
            extraAmount = VssMaterialPlan.Applicable(__instance, recipe.m_resources).First(r => r.m_resItem.m_itemData.m_shared.m_name == sample.m_shared.m_name).m_extraAmountOnlyOneIngredient;
        }
    }
}
[HarmonyPatch(typeof(Recipe), "GetAmount")]
internal static class VssAlternateAmountPatch
{
    private static bool Prefix(Recipe __instance, int quality, int craftMultiplier, ref int need, ref ItemDrop.ItemData singleReqItem, ref int __result)
    {
        if (!__instance.m_requireOnlyOneIngredient || Player.m_localPlayer == null ||
            (VssResources.Current?.Player != Player.m_localPlayer && VssContext.ForPlayer(Player.m_localPlayer) == null)) return true;
        singleReqItem = Player.m_localPlayer.GetFirstRequiredItem(Player.m_localPlayer.GetInventory(), __instance, quality, out need, out var extra, craftMultiplier);
        var itemQuality = singleReqItem?.m_quality ?? 1;
        __result = checked((__instance.m_amount + Mathf.CeilToInt((itemQuality - 1) * __instance.m_amount * __instance.m_qualityResultAmountMultiplier) + extra) * craftMultiplier);
        return false;
    }
}
[HarmonyPatch(typeof(Player), "ConsumeResources")]
internal static class VssResourcePaymentPatch
{
    private static bool Prefix(Player __instance, Piece.Requirement[] requirements)
    {
        var plan = VssResources.Current;
        if (plan == null || plan.Player != __instance || plan.Requirements != requirements) return true;
        if (!plan.Paid) plan.Pay();
        return false;
    }
}
[HarmonyPatch(typeof(Inventory), "RemoveItem", new[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
internal static class VssSinglePaymentPatch
{
    private static bool Prefix(Inventory __instance, string name, int amount, int itemQuality)
    {
        var plan = VssResources.Current;
        if (plan?.Single == null || __instance != plan.PlayerInventory || plan.Single.Name != name || plan.Single.Amount != amount || plan.Single.Quality != itemQuality) return true;
        if (!plan.Paid) plan.Pay();
        return false;
    }
}
[HarmonyPatch(typeof(Inventory), "ItemCheated", new[] { typeof(Piece.Requirement[]), typeof(int), typeof(bool) })]
internal static class VssMaterialCheatPatch
{
    private static void Postfix(Inventory __instance, ref bool __result)
    {
        var plan = VssResources.Current;
        if (plan != null && plan.PlayerInventory == __instance) __result |= plan.Debits.Any(d => d.Item.m_cheated);
    }
}
