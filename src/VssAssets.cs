using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace VSS;

internal static class VssAssets
{
    private const string BundleName = "vss_assets";
    private const string StandPrefabName = "vss_access_stand";

    private static GameObject _standModel;
    private static AssetBundle _bundle;
    internal static bool HasStandModel => _standModel != null;
    internal static Sprite Panel;
    internal static Sprite Wood, Inset;
    internal static Sprite ControllerIcon, ChestIcon;

    public static GameObject GetStandModel()
    {
        return _standModel != null ? Object.Instantiate(_standModel) : VssProceduralStand.CreateModel();
    }

    public static void Load(ManualLogSource log)
    {
        var pluginDir = Path.GetDirectoryName(typeof(VssPlugin).Assembly.Location);
        if (string.IsNullOrEmpty(pluginDir))
        {
            return;
        }

        var uiDir = Path.Combine(pluginDir, "ui");
        var controller = LoadTexture(Path.Combine(pluginDir, "icons", "controller.png"), log);
        if (controller != null) ControllerIcon = WholeSprite(controller, "Controller icon");
        var chestIcon = LoadTexture(Path.Combine(pluginDir, "icons", "chest-preference.png"), log);
        if (chestIcon != null) ChestIcon = WholeSprite(chestIcon, "Chest preference icon");
        var frameTexture = LoadTexture(Path.Combine(uiDir, "outer-frame.png"), log);
        if (frameTexture != null)
        {
            // The whole isolated frame is nine-sliced once. Its alpha hole contains no controls,
            // and the fixed 72-unit border keeps all four corner ornaments out of the layout.
            var border = Mathf.Ceil(Mathf.Min(frameTexture.width, frameTexture.height) * 0.16f);
            Panel = WholeSprite(frameTexture, "Outer frame", border * 100f / 72f, border);
        }
        var backing = LoadTexture(Path.Combine(uiDir, "backing.png"), log);
        if (backing != null) Wood = WholeSprite(backing, "Continuous oak backing");
        var inset = LoadTexture(Path.Combine(uiDir, "panel-skin.png"), log);
        if (inset != null) Inset = WholeSprite(inset, "Blank panel skin", 1000f, Mathf.Ceil(inset.width * 0.055f));

        var path = Path.Combine(pluginDir, BundleName);
        if (!File.Exists(path))
        {
            log.LogInfo("No vss_assets bundle found; using generated access stand model.");
            return;
        }

        _bundle = AssetBundle.LoadFromFile(path);
        if (_bundle == null)
        {
            log.LogWarning("Could not load vss_assets bundle; using generated access stand model.");
            return;
        }

        _standModel = _bundle.LoadAsset<GameObject>(StandPrefabName);
        if (_standModel == null)
        {
            log.LogWarning("vss_assets did not contain prefab vss_access_stand; using generated access stand model.");
        }
        else log.LogInfo("Loaded VSS_StorageInventory_001 pedestal model from vss_assets.");
    }

    private static Texture2D LoadTexture(string path, ManualLogSource log)
    {
        if (!File.Exists(path)) { log.LogWarning("Missing UI layer: " + path); return null; }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path))) { Object.Destroy(texture); return null; }
        texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
        texture.name = "VSS " + Path.GetFileNameWithoutExtension(path);
        return texture;
    }
    private static Sprite WholeSprite(Texture2D texture, string name, float pixelsPerUnit = 100f, float border = 0)
    {
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.one * border);
        sprite.name = "VSS " + name;
        return sprite;
    }
}
