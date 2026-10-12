using System;
using System.Collections.Generic;
using System.Linq;

namespace VSS;

internal sealed class VssNeed
{
    internal string Name;
    internal int Amount;
    internal int Quality = -1;
    internal VssNeed(string name, int amount, int quality = -1) { Name = name; Amount = amount; Quality = quality; }
    internal bool Matches(ItemDrop.ItemData item) => item != null && item.m_stack > 0 && !item.m_equipped &&
        item.m_shared.m_name == Name && (Quality < 0 || item.m_quality == Quality) && item.m_worldLevel >= Game.m_worldLevel;
}

internal sealed class VssMaterialPlan
{
    internal sealed class Debit
    {
        internal Inventory Inventory;
        internal ItemDrop.ItemData Item;
        internal VssItemKey Key;
        internal int Amount;
    }
    internal readonly List<Debit> Debits = new();
    internal Inventory PlayerInventory;
    internal Player Player;
    internal Piece.Requirement[] Requirements;
    internal VssNeed Single;
    internal ItemDrop.ItemData SingleSample;
    internal bool Paid;
    internal IEnumerable<Inventory> ChestInventories => Debits.Where(d => d.Inventory != PlayerInventory).Select(d => d.Inventory).Distinct();
    internal bool UsesStorage => ChestInventories.Any();
    internal static VssMaterialPlan Build(Inventory inventory, IEnumerable<Inventory> storage, IEnumerable<VssNeed> needs, ItemDrop.ItemData exclude = null)
    {
        var plan = new VssMaterialPlan { PlayerInventory = inventory };
        var sources = (inventory != null ? new[] { inventory } : Array.Empty<Inventory>()).Concat(storage).Distinct().ToList();
        var allocated = new Dictionary<ItemDrop.ItemData, int>();
        foreach (var need in needs)
        {
            var left = need.Amount;
            if (left <= 0) continue;
            foreach (var source in sources)
            {
                foreach (var item in source.GetAllItems())
                {
                    if (item == exclude || !need.Matches(item)) continue;
                    allocated.TryGetValue(item, out var taken);
                    var amount = Math.Min(left, item.m_stack - taken);
                    if (amount <= 0) continue;
                    plan.Debits.Add(new Debit { Inventory = source, Item = item, Key = new VssItemKey(item), Amount = amount });
                    allocated[item] = taken + amount; left -= amount;
                    if (left <= 0) break;
                }
                if (left <= 0) break;
            }
            if (left > 0) return null;
        }
        return plan;
    }
    internal bool Validate() => !Paid && Debits.GroupBy(d => d.Item).All(group =>
        group.All(d => d.Inventory.ContainsItem(d.Item) && d.Key.Matches(d.Item) && !d.Item.m_equipped) && group.Key.m_stack >= group.Sum(d => d.Amount));
    internal void Pay()
    {
        if (!Validate()) throw new InvalidOperationException("VSS payment plan became invalid before commit.");
        foreach (var debit in Debits) debit.Item.m_stack -= debit.Amount;
        var inventories = Debits.Select(d => d.Inventory).Distinct().ToList();
        foreach (var inventory in inventories) inventory.GetAllItems().RemoveAll(i => i.m_stack <= 0);
        Paid = true;
        foreach (var inventory in inventories) VssGame.Notify(inventory);
    }
    internal static IEnumerable<Piece.Requirement> Applicable(Player player, Piece.Requirement[] requirements) => requirements.Where(r =>
        r != null && r.m_resItem != null && r.m_upgraderResource == (player.GetCurrentCraftingStation() != null && player.GetCurrentCraftingStation().m_upgrader));
    internal static List<VssNeed> Needs(Player player, Piece.Requirement[] requirements, int quality, int multiplier) => Applicable(player, requirements)
        .Select(r => new VssNeed(r.m_resItem.m_itemData.m_shared.m_name, checked(r.GetAmount(quality) * multiplier))).Where(n => n.Amount > 0).ToList();
    internal static VssMaterialPlan ForRecipe(Player player, Recipe recipe, int quality, int multiplier, List<Inventory> storage, ItemDrop.ItemData exclude)
    {
        if (!recipe.m_requireOnlyOneIngredient)
        {
            var normal = Build(player.GetInventory(), storage, Needs(player, recipe.m_resources, quality, multiplier), exclude);
            if (normal != null) { normal.Player = player; normal.Requirements = recipe.m_resources; }
            return normal;
        }
        foreach (var req in Applicable(player, recipe.m_resources))
        {
            var count = checked(req.GetAmount(quality) * multiplier);
            if (count <= 0) continue;
            for (var q = 0; q <= req.m_resItem.m_itemData.m_shared.m_maxQuality; q++)
            {
                var need = new VssNeed(req.m_resItem.m_itemData.m_shared.m_name, count, q);
                var single = Build(player.GetInventory(), storage, new[] { need }, exclude);
                if (single == null) continue;
                single.Player = player; single.Requirements = recipe.m_resources; single.Single = need;
                single.SingleSample = single.Debits.First().Item.Clone();
                return single;
            }
        }
        return null;
    }
}

internal static class VssResources
{
    internal static VssMaterialPlan Current;
    private static Player _cachedPlayer;
    private static VssAccessStand _cachedStand;
    private static UnityEngine.Vector3 _cachedOrigin;
    private static int _generation;
    private static float _until;
    private static readonly List<Inventory> Cache = new();
    internal static List<Inventory> Inventories(Player player)
    {
        if (Current != null && Current.Player == player) return Current.ChestInventories.ToList();
        var context = VssContext.ForPlayer(player, UseCraftingStation);
        if (context == null) return new List<Inventory>();
        if (_cachedPlayer != player || _cachedStand != context.Stand || _generation != VssTransactions.Generation ||
            UnityEngine.Time.unscaledTime >= _until || (context.Origin - _cachedOrigin).sqrMagnitude > 0.25f)
        {
            Cache.Clear(); Cache.AddRange(VssStorageScanner.Sources(context).Select(c => c.GetInventory()));
            _cachedPlayer = player; _cachedStand = context.Stand; _cachedOrigin = context.Origin;
            _generation = VssTransactions.Generation; _until = UnityEngine.Time.unscaledTime + 0.35f;
        }
        return Cache;
    }
    internal static int Stored(Player player, string name, int quality, bool world)
    {
        var amount = 0;
        foreach (var inventory in Inventories(player))
            foreach (var item in inventory.GetAllItems())
                if (item.m_stack > 0 && !item.m_equipped && (name == null || item.m_shared.m_name == name) &&
                    (quality < 0 || item.m_quality == quality) && (!world || item.m_worldLevel >= Game.m_worldLevel))
                    amount = checked(amount + item.m_stack);
        return amount;
    }
    internal sealed class Scope : IDisposable
    {
        private readonly Player _previous;
        private readonly bool _previousStation;
        internal Scope(Player player, bool useCraftingStation = true)
        { _previous = Reading; _previousStation = UseCraftingStation; Reading = player; UseCraftingStation = useCraftingStation; }
        public void Dispose() { Reading = _previous; UseCraftingStation = _previousStation; }
    }
    internal static Player Reading;
    internal static bool UseCraftingStation = true;
}
