using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace VSS;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class VssPlugin : BaseUnityPlugin
{
    public const string ModGuid = "georo.vss";
    public const string ModName = "VSS";
    public const string ModVersion = "0.3.1";

    internal static ManualLogSource Log;
    internal static VssConfig ModConfig;
    internal static VssPlugin Instance;

    private Harmony _harmony;

    private void Awake()
    {
        Log = Logger;
        Instance = this;
        ModConfig = new VssConfig(Config);
        ModConfig.Radius.SettingChanged += (_, __) => VssSettings.Broadcast();
        ModConfig.AutoRefillExcludedItems.SettingChanged += (_, __) => VssSettings.Broadcast();

        _harmony = new Harmony(ModGuid);
        _harmony.PatchAll();

        VssAssets.Load(Logger);
        VssUi.EnsureCreated();
        Logger.LogInfo($"VSS {ModVersion} loaded. Storage radius: {ModConfig.Radius.Value}m. Multiplayer requires VSS on every player and the server.");
        VssRuntimeTests.Run();
    }

    private void Update() { VssSettings.Tick(); VssRefillBridge.Tick(); }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        VssMessageOverlay.Restore();
        VssTransfer.Cancel();
    }
}
