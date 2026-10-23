using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class InscriptionSurface
{
    internal static readonly Color Stone = new(52, 58, 60, 255);

    internal static Geometry Build(Vector3 foot, Color ink)
    {
        var g = new Geometry();
        g.Box(foot + new Vector3(0, .09f, 0), new(1.8f, .18f, .65f), new(43, 48, 50, 255));
        Vector3 P(float x, float y, float z) => foot + new Vector3(x, y, z);
        // Replace the front face itself: no floating decal or coplanar overlay.
        Color face = new(ink.R, ink.G, ink.B, (byte)254);
        g.Quad(P(-.7f, .18f, .16f), P(.7f, .18f, .16f), P(.7f, 2, .16f), P(-.7f, 2, .16f), face);
        g.Quad(P(.7f, .18f, -.16f), P(-.7f, .18f, -.16f), P(-.7f, 2, -.16f), P(.7f, 2, -.16f), Stone);
        g.Quad(P(-.7f, .18f, -.16f), P(-.7f, .18f, .16f), P(-.7f, 2, .16f), P(-.7f, 2, -.16f), Stone);
        g.Quad(P(.7f, .18f, .16f), P(.7f, .18f, -.16f), P(.7f, 2, -.16f), P(.7f, 2, .16f), Stone);
        g.Quad(P(-.7f, 2, .16f), P(.7f, 2, .16f), P(.7f, 2, -.16f), P(-.7f, 2, -.16f), Stone);
        g.Quad(P(-.7f, .18f, -.16f), P(.7f, .18f, -.16f), P(.7f, .18f, .16f), P(-.7f, .18f, .16f), Stone);
        return g;
    }

    internal static Vector2 Coordinates(Vector3 point, Vector3 foot, Vector3 facing)
        => new(Vector3.Dot(point - foot, new Vector3(facing.Z, 0, -facing.X)) / 1.4f + .5f,
            (2 - (point.Y - foot.Y)) / 1.82f);

    internal static unsafe Texture2D CreateTexture()
    {
        const int width = 512, height = 768;
        // A small invented alphabet, repeated as words rather than random noise.
        // Each symbol uses a stable selection of angular carved strokes.
        (Vector2 A, Vector2 B)[] strokes =
        [
            (new(0, 0), new(0, 1)), (new(1, 0), new(1, 1)),
            (new(.5f, 0), new(.5f, 1)), (new(0, 0), new(1, 0)),
            (new(0, .5f), new(1, .5f)), (new(0, 1), new(1, 1)),
            (new(0, 0), new(1, 1)), (new(1, 0), new(0, 1)),
            (new(0, 0), new(1, .5f)), (new(1, .5f), new(0, 1)),
            (new(1, 0), new(0, .5f)), (new(0, .5f), new(1, 1)),
            (new(.5f, 0), new(1, .25f)), (new(.5f, .5f), new(1, .75f))
        ];
        int[][] alphabet =
        [
            [0, 8, 9], [2, 12, 13], [3, 7, 5], [0, 4, 7], [1, 10, 11],
            [2, 8, 11], [0, 3, 4], [6, 7, 2], [0, 8, 4], [1, 7, 5],
            [3, 2, 13], [0, 6, 1], [10, 11, 4], [2, 3, 5], [0, 9, 12],
            [6, 4, 5], [1, 8, 9], [0, 4, 5, 12], [3, 7, 13], [2, 10, 9]
        ];
        // Unequal lines and spaces give the visual rhythm of an actual passage.
        string[] lines =
        [
            "ABGKD HNCE", "FJML CBAG RE", "NKDE AFH JM", "GLCB NEK",
            "HMAF DCLG KB", "REJN BGKD", "CNHA EFML DJ", "ABGK HNCE",
            "FJML RE NKD", "GLCB NEK HMA", "DC LGKB REJN", "CNHA EFML"
        ];
        var mask = new float[width * height];
        void Line(Vector2 a, Vector2 b, float thickness)
        {
            Vector2 delta = b - a;
            int left = Math.Max(0, (int)MathF.Floor(Math.Min(a.X, b.X) - thickness));
            int right = Math.Min(width - 1, (int)MathF.Ceiling(Math.Max(a.X, b.X) + thickness));
            int top = Math.Max(0, (int)MathF.Floor(Math.Min(a.Y, b.Y) - thickness));
            int bottom = Math.Min(height - 1, (int)MathF.Ceiling(Math.Max(a.Y, b.Y) + thickness));
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                {
                    Vector2 p = new(x + .5f, y + .5f);
                    float t = Math.Clamp(Vector2.Dot(p - a, delta) / delta.LengthSquared(), 0, 1);
                    float coverage = Math.Clamp(thickness / 2 + .5f - Vector2.Distance(p, a + delta * t), 0, 1);
                    mask[y * width + x] = Math.Max(mask[y * width + x], coverage);
                }
        }
        void Glyph(char letter, Vector2 origin, Vector2 size, float thickness)
        {
            foreach (int stroke in alphabet[(letter - 'A') % alphabet.Length])
                Line(origin + strokes[stroke].A * size, origin + strokes[stroke].B * size, thickness);
        }
        const string heading = "HNAKGE";
        for (int i = 0; i < heading.Length; i++) Glyph(heading[i], new(91 + i * 57, 64), new(32, 51), 4);
        for (int row = 0; row < lines.Length; row++)
        {
            float x = 51;
            foreach (char letter in lines[row])
            {
                if (letter == ' ') { x += 15; continue; }
                Glyph(letter, new(x, 172 + row * 43), new(20, 27), 2.8f);
                x += 30;
            }
        }
        var image = Raylib.GenImageColor(width, height, Color.Black);
        try
        {
            var pixels = (Color*)image.Data;
            for (int i = 0; i < mask.Length; i++)
            {
                // Tiny deterministic losses suggest worn carving without breaking the letters.
                float wear = ((i * 17L + i / width * 31L) % 97) < 4 ? .65f : 1;
                byte value = (byte)(mask[i] * wear * 255);
                pixels[i] = new(value, value, value, (byte)255);
            }
            Raylib.ImageMipmaps(ref image);
            var texture = Raylib.LoadTextureFromImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            Raylib.SetTextureWrap(texture, TextureWrap.Clamp);
            return texture;
        }
        finally { Raylib.UnloadImage(image); }
    }
}
