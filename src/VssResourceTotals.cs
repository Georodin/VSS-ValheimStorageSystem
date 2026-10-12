using System;
using System.Collections.Generic;

namespace VSS;

// Availability display only. Actual payments still plan against fresh, leased inventories.
internal sealed class VssResourceTotals
{
    private readonly struct Key : IEquatable<Key>
    {
        private readonly string _name;
        private readonly int _quality;
        private readonly bool _world;
        internal Key(string name, int quality, bool world) { _name = name; _quality = quality; _world = world; }
        public bool Equals(Key other) => _name == other._name && _quality == other._quality && _world == other._world;
        public override bool Equals(object obj) => obj is Key key && Equals(key);
        public override int GetHashCode() => unchecked(((_name?.GetHashCode() ?? 0) * 397 ^ _quality) * 397 ^ (_world ? 1 : 0));
    }
    private readonly Dictionary<Key, int> _counts = new();
    private int _worldLevel;
    internal void Reset(int worldLevel) { _counts.Clear(); _worldLevel = worldLevel; }
    internal void Add(ItemDrop.ItemData item)
    {
        if (item == null || item.m_equipped || item.m_stack <= 0) return;
        Add(item.m_shared.m_name, item.m_quality, false, item.m_stack);
        if (item.m_worldLevel >= _worldLevel) Add(item.m_shared.m_name, item.m_quality, true, item.m_stack);
    }
    private void Add(string name, int quality, bool world, int amount)
    {
        Increment(new Key(name, quality, world), amount);
        if (quality >= 0) Increment(new Key(name, -1, world), amount);
        if (name != null)
        {
            Increment(new Key(null, quality, world), amount);
            if (quality >= 0) Increment(new Key(null, -1, world), amount);
        }
    }
    private void Increment(Key key, int amount) { _counts.TryGetValue(key, out var previous); _counts[key] = checked(previous + amount); }
    internal int Count(string name, int quality, bool world) => _counts.TryGetValue(new Key(name, quality < 0 ? -1 : quality, world), out var count) ? count : 0;
}
