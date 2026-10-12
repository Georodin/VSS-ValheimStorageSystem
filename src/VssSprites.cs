using UnityEngine;

namespace VSS;

internal static class VssSprites
{
    private static Sprite _standIcon;

    public static Sprite StandIcon => VssAssets.ControllerIcon != null ? VssAssets.ControllerIcon : _standIcon ??= CreateStandIcon();

    private static Sprite CreateStandIcon()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var clear = new Color(0f, 0f, 0f, 0f);
        var wood = new Color(0.48f, 0.29f, 0.13f, 1f);
        var page = new Color(0.86f, 0.78f, 0.58f, 1f);
        var ink = new Color(0.08f, 0.06f, 0.04f, 1f);
        var quill = new Color(0.9f, 0.88f, 0.78f, 1f);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                texture.SetPixel(x, y, clear);
            }
        }

        Fill(texture, 10, 10, 44, 10, wood);
        Fill(texture, 15, 20, 34, 8, wood);
        Fill(texture, 18, 30, 28, 18, page);
        Fill(texture, 31, 30, 2, 18, ink);

        for (var i = 0; i < 24; i++)
        {
            var x = 39 + i / 2;
            var y = 42 - i;
            if (x >= 0 && x < size && y >= 0 && y < size)
            {
                texture.SetPixel(x, y, quill);
                texture.SetPixel(x + 1, y, quill);
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static void Fill(Texture2D texture, int x, int y, int width, int height, Color color)
    {
        for (var yy = y; yy < y + height; yy++)
        {
            for (var xx = x; xx < x + width; xx++)
            {
                texture.SetPixel(xx, yy, color);
            }
        }
    }
}
