using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VSS;

// Export only from the isolated diagnostics process, for local art direction. Never ship vanilla artwork.
internal static class VssVanillaReferences
{
    internal static void Export(ZNetScene scene)
    {
        if (!Environment.GetCommandLineArgs().Contains("-vss-preview")) return;
        var names = new[] { "piece_chest_wood", "piece_chair", "piece_workbench", "forge", "Wood", "Hammer", "Resin", "Coal" };
        var sheet = new Texture2D(1024, 512, TextureFormat.RGBA32, false);
        var colors = new Color[1024 * 512];
        for (var i = 0; i < colors.Length; i++) colors[i] = new Color(0.12f, 0.115f, 0.10f, 1);
        sheet.SetPixels(colors);
        for (var n = 0; n < names.Length; n++)
        {
            var prefab = scene.GetPrefab(names[n]);
            var sprite = prefab?.GetComponent<Piece>()?.m_icon ?? prefab?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            if (sprite == null) continue;
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(sprite.texture, rt); RenderTexture.active = rt;
                var rect = sprite.textureRect;
                var readable = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(rect, 0, 0); readable.Apply();
                for (var y = 0; y < 224; y++) for (var x = 0; x < 224; x++)
                {
                    var c = readable.GetPixelBilinear(x / 223f, y / 223f);
                    var sx = (n % 4) * 256 + 16 + x; var sy = (1 - n / 4) * 256 + 16 + y;
                    var b = sheet.GetPixel(sx, sy); sheet.SetPixel(sx, sy, Color.Lerp(b, c, c.a));
                }
                UnityEngine.Object.Destroy(readable);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        }
        sheet.Apply();
        var root = Path.GetDirectoryName(typeof(VssPlugin).Assembly.Location);
        var path = Path.GetFullPath(Path.Combine(root, "../../../VSS-ValheimStorageSystem/tests/runtime/vanilla-icon-reference.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);
    }
}
