using System.Collections.Generic;
using Backlands.Model;
using Godot;

namespace Backlands.Scene;

/// O bonequinho desenhado em código para quem ainda não tem folha de sprites.
internal static class PlaceholderTexture
{
    // --- Placeholder procedural -------------------------------------------

    public static ImageTexture Create(uint entityId, bool isLocal, EntityKind kind)
    {
        const int width = 20;
        const int height = 32;

        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));

        // Matiz a partir do id, com passo da razão áurea: ids consecutivos
        // saem em cores bem distantes.
        var hue = (entityId * 0.618033988f) % 1.0f;
        var body = isLocal ? new Color(0.95f, 0.85f, 0.35f) : Color.FromHsv(hue, 0.55f, 0.85f);
        var skin = new Color(0.95f, 0.78f, 0.62f);
        var outline = new Color(0.08f, 0.07f, 0.10f);

        if (kind == EntityKind.Monster)
        {
            var blob = Color.FromHsv(hue, 0.7f, 0.9f);
            FillRect(image, 3, 14, 14, 16, blob);
            FillRect(image, 5, 11, 10, 4, blob);
            FillRect(image, 6, 19, 2, 3, outline);
            FillRect(image, 12, 19, 2, 3, outline);
        }
        else
        {
            FillRect(image, 6, 2, 8, 10, skin);
            FillRect(image, 5, 12, 10, 13, body);
            FillRect(image, 6, 25, 3, 7, outline);
            FillRect(image, 11, 25, 3, 7, outline);
        }

        var outlinePixels = new List<Vector2I>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (image.GetPixel(x, y).A <= 0.0f && HasOpaqueNeighbour(image, x, y))
                {
                    outlinePixels.Add(new Vector2I(x, y));
                }
            }
        }
        foreach (var pixel in outlinePixels)
        {
            image.SetPixel(pixel.X, pixel.Y, outline);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static void FillRect(Image image, int x, int y, int width, int height, Color color)
    {
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                var px = x + dx;
                var py = y + dy;
                if (px >= 0 && py >= 0 && px < image.GetWidth() && py < image.GetHeight())
                {
                    image.SetPixel(px, py, color);
                }
            }
        }
    }

    private static bool HasOpaqueNeighbour(Image image, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var px = x + dx;
                var py = y + dy;
                if (px < 0 || py < 0 || px >= image.GetWidth() || py >= image.GetHeight())
                {
                    continue;
                }
                if (image.GetPixel(px, py).A > 0.5f)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
