using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace VSS;
public sealed class VssConfig
{
    public ConfigEntry<bool> RegisterStand { get; }
    public ConfigEntry<float> Radius { get; }
    public ConfigEntry<bool> ModdedContainers { get; }
    public ConfigEntry<string> ExcludedContainers { get; }
    public ConfigEntry<float> RefreshSeconds { get; }
    public HashSet<string> Excluded { get; } = new(StringComparer.Ordinal);
    public VssConfig(ConfigFile config)
    {
        RegisterStand = config.Bind("General", "RegisterStand", true, "Register the Storage Inventory pedestal in the hammer Furniture tab.");
        Radius = config.Bind("Storage", "RadiusMeters", 20f, new ConfigDescription(
            "Chest radius in metres. In multiplayer the host/server value is used by everyone.", new AcceptableValueRange<float>(1f, 100f)));
        ModdedContainers = config.Bind("Storage", "ModdedContainers", true,
            "Also link containers from other mods that players build with the hammer. Ship and cart storage, graves, the Obliterator and world-generated chests stay excluded. Use the same value on every player.");
        ExcludedContainers = config.Bind("Storage", "ExcludedContainers", "",
            "Comma-separated container prefab names that are never linked, for example piece_chest_private. Linked modded containers are named in the log. Use the same value on every player.");
        RefreshSeconds = config.Bind("UI", "RefreshSeconds", 0.75f, new ConfigDescription(
            "Storage display refresh interval.", new AcceptableValueRange<float>(0.25f, 5f)));
        ParseExcluded();
        ExcludedContainers.SettingChanged += (_, __) => ParseExcluded();
    }
    private void ParseExcluded()
    {
        Excluded.Clear();
        foreach (var name in (ExcludedContainers.Value ?? "").Split(','))
            if (name.Trim().Length > 0) Excluded.Add(name.Trim());
    }
}
