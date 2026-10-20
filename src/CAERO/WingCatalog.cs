using System.Numerics;
using Raylib_cs;

namespace Caero;

// Each module extends from x=0 to width. Multiple distinct modules form one island's skyline.
internal static class WingCatalog
{
    internal const int StyleCount = 20;
    internal static readonly string[] Names = ["Arcade", "Setbacks", "Sawtooth", "Fingers", "Courtyard",
        "Fork", "Offset rooms", "Dome", "Shelves", "Twin galleries", "Fan", "Stepped pyramid",
        "Round apse", "Perforated wall", "Flying buttresses", "Pergola", "Hanging teeth", "Cross pavilion",
        "Drum cluster", "Crowned hall"];

    internal static Geometry Build(int style, float width, float depth, float height, Color stone, Color pale, Color floor)
    {
        if ((uint)style >= StyleCount) throw new ArgumentOutOfRangeException(nameof(style));
        var g = new Geometry();
        void B(float x, float y, float z, float w, float h, float d, bool light = false, float yaw = 0)
            => g.Box(new(x, y, z), new(w, h, d), light ? pale : stone, yaw);
        void Slab(float x, float z, float w, float d, float y)
            => g.Box(new(x, y - .3f, z), new(w, .6f, d), floor);
        void Pillars(float x0, float x1, float z0, float z1, float top)
        {
            foreach (float x in new[] { x0, x1 }) foreach (float z in new[] { z0, z1 })
                B(x, (top - .6f) / 2, z, .6f, top - .6f, .6f, true);
        }
        float w = width, d = depth, h = height;
        Slab(w / 2, 0, w, d, 0);
        switch (style)
        {
            case 0: // Long colonnade with a flat cornice.
                for (int i = 0; i < 5; i++) foreach (float s in new[] { -1f, 1f })
                    B(.4f + (w - .8f) * i / 4, (h - .6f) / 2, s * (d / 2 - .4f), .65f, h - .6f, .65f, true);
                Slab(w / 2, 0, w, d, h); break;
            case 1: // Three diminishing terraces.
                for (int i = 0; i < 3; i++) B(w * (.5f - i * .09f), h * (i + .5f) / 3, 0,
                    w * (1 - i * .2f), h / 3, d * (1 - i * .22f));
                break;
            case 2: // Sawtooth profile.
                for (int i = 0; i < 5; i++) B(w * (i + .5f) / 5, h * (.35f + .12f * (i % 2)), 0,
                    w / 5 - .08f, h * (.7f + .24f * (i % 2)), d);
                break;
            case 3: // Narrow parallel fingers.
                for (int i = -1; i <= 1; i++)
                {
                    float reach = w * (i == 0 ? 1 : .7f);
                    B(reach / 2, h * .35f, i * d * .33f, reach, h * .7f, d * .2f);
                    Slab(reach / 2, i * d * .33f, reach, d * .25f, h * .7f);
                }
                break;
            case 4: // Roofless courtyard.
                B(.35f, h / 2, 0, .7f, h, d); B(w - .35f, h / 2, 0, .7f, h, d);
                foreach (float s in new[] { -1f, 1f }) B(w / 2, h / 2, s * (d / 2 - .35f), w - 1.4f, h, .7f);
                break;
            case 5: // Two tall branches separated by a slot.
                foreach (float s in new[] { -1f, 1f })
                { B(w / 2, h / 2, s * d * .32f, w, h, d * .25f); Slab(w / 2, s * d * .32f, w, d * .3f, h); }
                break;
            case 6: // Alternating rooms.
                for (int i = 0; i < 4; i++) B(w * (i + .5f) / 4, h * (.35f + i * .06f),
                    (i % 2 == 0 ? -1 : 1) * d * .18f, w / 4 - .05f, h * (.7f + i * .12f), d * .6f);
                break;
            case 7: // Domed pavilion.
                Pillars(.4f, w - .4f, -d / 2 + .4f, d / 2 - .4f, h * .55f);
                g.Ellipsoid(new(w / 2, h * .55f, 0), new(w / 2, h * .4f, d / 2), pale, 16, 8);
                break;
            case 8: // Thin stacked shelves.
                B(w * .25f, h / 2, 0, .8f, h, d * .5f);
                for (int i = 1; i <= 4; i++) Slab(w / 2, 0, w, d, h * i / 4);
                break;
            case 9: // Two open galleries.
                Pillars(.4f, w - .4f, -d / 2 + .4f, d / 2 - .4f, h);
                Slab(w / 2, 0, w, d, h / 2); Slab(w / 2, 0, w, d, h); break;
            case 10: // Fan of diagonal blades.
                for (int i = -2; i <= 2; i++) B(w / 2, h * .3f, i * d * .15f,
                    w * .8f, h * .6f, .35f, true, i * .08f);
                break;
            case 11: // Four-tier pyramid stretched sideways.
                for (int i = 0; i < 4; i++) B(w / 2, h * (i + .5f) / 4, 0, w * (1 - i * .21f), h / 4, d * (1 - i * .2f));
                break;
            case 12: // Rounded end hall.
                B(w * .3f, h * .35f, 0, w * .6f, h * .7f, d * .7f);
                g.Cylinder(new(w * .68f, 0, 0), Math.Min(w * .3f, d * .48f), Math.Min(w * .3f, d * .48f), h, pale, 20);
                break;
            case 13: // Lattice of rectangular voids.
                for (int i = 0; i <= 4; i++) B(.3f + (w - .6f) * i / 4, (h - .6f) / 2, 0, .5f, h - .6f, d * .45f);
                for (int i = 1; i <= 3; i++) B(w / 2, h * i / 3 - .25f, 0, w, .5f, d * .45f, true);
                break;
            case 14: // Buttress fins stepping outward.
                for (int i = 0; i < 5; i++) B(w * (i + .5f) / 5, h * (1 - i * .13f) / 2, 0,
                    .6f, h * (1 - i * .13f), d, true);
                B(w / 2, h * .22f, 0, w, .7f, d * .3f); break;
            case 15: // Open pergola.
                Pillars(.4f, w - .4f, -d / 2 + .4f, d / 2 - .4f, h);
                for (int i = 0; i < 7; i++) B(w * (i + .5f) / 7, h - .3f, 0, w / 12, .6f, d, true);
                break;
            case 16: // Roof with hanging teeth.
                Pillars(.4f, w - .4f, -d / 2 + .4f, d / 2 - .4f, h); Slab(w / 2, 0, w, d, h);
                for (int i = 0; i < 5; i++) B(w * (i + .5f) / 5, h * .68f, 0, w / 8, h * .5f, d * .6f);
                break;
            case 17: // Cross-shaped pavilion.
                B(w / 2, h * .3f, 0, w, h * .6f, d * .32f);
                B(w / 2, h * .45f, 0, w * .28f, h * .9f, d); break;
            case 18: // Row of cylindrical drums.
                for (int i = 0; i < 3; i++)
                    g.Cylinder(new(w * (i + .5f) / 3, 0, 0), Math.Min(w / 7, d * .45f), Math.Min(w / 7, d * .35f), h * (.6f + i * .2f), stone, 12);
                break;
            case 19: // Hall with an asymmetric crown.
                B(w / 2, h * .3f, 0, w, h * .6f, d * .8f);
                for (int i = 0; i < 4; i++) B(w * (.15f + i * .22f), h * (.7f + i * .04f), 0,
                    w * .12f, h * (.2f + i * .08f), d * .9f, true);
                break;
        }
        return g;
    }
}
