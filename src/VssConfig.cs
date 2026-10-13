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
    public ConfigEntry<KeyboardShortcut> AutoCollectToggleKey { get; }
    internal static KeyboardShortcut MigrateRefillKey(KeyboardShortcut key) =>
        key.Equals(new KeyboardShortcut(KeyCode.G)) ? new KeyboardShortcut(KeyCode.F6) : key;
    public VssConfig(ConfigFile config)
    {
        RegisterStand = config.Bind("General", "RegisterStand", true, "Register the Storage Inventory pedestal in the hammer Furniture tab.");
        Radius = config.Bind("Storage", "RadiusMeters", 20f, new ConfigDescription(
            "Chest radius in metres. In multiplayer the host/server value is used by everyone.", new AcceptableValueRange<float>(1f, 100f)));
        RefreshSeconds = config.Bind("UI", "RefreshSeconds", 0.75f, new ConfigDescription(
            "Storage display refresh interval.", new AcceptableValueRange<float>(0.25f, 5f)));
        AutoRefillExcludedItems = config.Bind("Storage", "AutoRefillExcludedItems", "FineWood,RoundLog,ElderBark,YggdrasilWood,Blackwood",
            "Additional prefab names excluded from automatic refilling/processing. Special woods are always protected from VSS station supplies, even if this list is empty. The host value applies to all peers.");
        AutoRefillToggleKey = config.Bind("Controls", "AutoRefillToggleKey", new KeyboardShortcut(KeyCode.F6),
            "Toggle automatic supplies for the refillable object you are facing. The storage controller master switch must also be on. Object choices start off and are saved/shared in co-op.");
        AutoRefillToggleKey.Value = MigrateRefillKey(AutoRefillToggleKey.Value);
        AutoCollectToggleKey = config.Bind("Controls", "AutoCollectToggleKey", new KeyboardShortcut(KeyCode.F7),
            "Toggle collection of finished output for the machine you are facing. Enable the separate auto-collect master in the storage window too. Both start off and are saved/shared in co-op.");
    }
}
