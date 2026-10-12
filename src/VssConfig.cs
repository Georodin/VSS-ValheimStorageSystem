using BepInEx.Configuration;
using UnityEngine;

namespace VSS;
public sealed class VssConfig
{
    public ConfigEntry<bool> RegisterStand { get; }
    public ConfigEntry<float> Radius { get; }
    public ConfigEntry<float> RefreshSeconds { get; }
    public ConfigEntry<string> AutoRefillExcludedItems { get; }
    public ConfigEntry<KeyboardShortcut> AutoRefillToggleKey { get; }
    public VssConfig(ConfigFile config)
    {
        RegisterStand = config.Bind("General", "RegisterStand", true, "Register the Storage Inventory pedestal in the hammer Furniture tab.");
        Radius = config.Bind("Storage", "RadiusMeters", 20f, new ConfigDescription(
            "Chest radius in metres. In multiplayer the host/server value is used by everyone.", new AcceptableValueRange<float>(1f, 100f)));
        RefreshSeconds = config.Bind("UI", "RefreshSeconds", 0.75f, new ConfigDescription(
            "Storage display refresh interval.", new AcceptableValueRange<float>(0.25f, 5f)));
        AutoRefillExcludedItems = config.Bind("Storage", "AutoRefillExcludedItems", "FineWood,RoundLog,ElderBark,YggdrasilWood,Blackwood",
            "Additional prefab names excluded from automatic refilling/processing. Special woods are always protected from VSS station supplies, even if this list is empty. The host value applies to all peers.");
        AutoRefillToggleKey = config.Bind("Controls", "AutoRefillToggleKey", new KeyboardShortcut(KeyCode.G),
            "Toggle automatic supplies for the refillable object you are facing. The storage controller master switch must also be on. Object choices start off and are saved/shared in co-op.");
    }
}
