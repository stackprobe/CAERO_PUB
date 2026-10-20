using System.Numerics;
using Raylib_cs;

namespace Caero;

// Authored silhouettes; geometry, palette, yaw and site use independent seeded choices.
internal static class SculptureCatalog
{
    internal const int StyleCount = 20, ColorCount = 20;
    internal const int StartStyle = 20;
    internal static readonly Color StartColor = new(104, 125, 152, 254);

    internal static Geometry BuildStartMonument()
    {
        // A unique blue-grey astronomical monument, with nested rings and a faceted heart.
        var g = new Geometry(); Color color = StartColor;
        g.Box(new(0, .3f, 0), new(4.8f, .6f, 4.8f), color);
        g.Box(new(0, .75f, 0), new(4.1f, .3f, 4.1f), color);
        g.Cylinder(new(0, .9f, 0), .45f, .32f, 4.2f, color, 16);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.PI / 2;
            var direction = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            g.Cylinder(direction * 1.6f + new Vector3(0, .9f, 0), .28f, .18f, 2.6f, color, 8);
            g.Box(direction * .8f + new Vector3(0, 3.35f, 0), new(2, .3f, .35f), color, -angle);
        }
        void Orbit(float inner, float outer, float yaw, float tilt)
        {
            var ring = new Geometry(); ring.Ring(Vector3.Zero, inner, outer, .35f, color, 40);
            var rotation = Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateRotationZ(tilt) * Matrix4x4.CreateRotationY(yaw);
            // Linear rotation keeps both vertex winding and shading normals intact.
            ring.Transform(p => Vector3.Transform(p, rotation), new(0, 6.3f, 0));
            g.Append(ring);
        }
        Orbit(2.7f, 3, 0, .18f); Orbit(1.95f, 2.2f, MathF.PI / 2, -.22f);
        g.Cylinder(new(0, 4.9f, 0), .12f, 1.15f, 1.4f, color, 8);
        g.Cylinder(new(0, 6.3f, 0), 1.15f, .12f, 1.4f, color, 8);
        for (int i = 0; i < 3; i++)
        {
            float angle = .4f + i * MathF.Tau / 3;
            var center = new Vector3(MathF.Cos(angle) * 3, 6.3f + MathF.Sin(angle) * 3, 0);
            g.Ellipsoid(center, new(.28f), color, 12, 8);
        }
        return g;
    }
    internal static readonly Color[] Palette = [
        new(89, 113, 93, 254), new(153, 103, 62, 254), new(181, 139, 70, 254), new(94, 70, 59, 254),
        new(66, 73, 83, 254), new(167, 179, 183, 254), new(197, 187, 157, 254), new(115, 47, 43, 254),
        new(163, 72, 48, 254), new(176, 131, 83, 254), new(46, 100, 96, 254), new(46, 80, 111, 254),
        new(65, 62, 112, 254), new(105, 69, 111, 254), new(139, 77, 100, 254), new(104, 119, 57, 254),
        new(64, 87, 56, 254), new(203, 201, 182, 254), new(62, 55, 52, 254), new(139, 155, 163, 254)
    ];

    internal static Geometry Build(int style, int colorIndex, Color plinth)
    {
        if ((uint)style >= StyleCount || (uint)colorIndex >= ColorCount) throw new ArgumentOutOfRangeException();
        var g = new Geometry(); Color color = Palette[colorIndex];
        void B(float x, float y, float z, float w, float h, float d, float yaw = 0)
            => g.Box(new(x, y, z), new(w, h, d), color, yaw);
        void C(float x, float y, float z, float radius, float top, float height, int sides = 12)
            => g.Cylinder(new(x, y, z), radius, top, height, color, sides);
        void Ball(float x, float y, float z, float radius)
            => g.Ellipsoid(new(x, y, z), new(radius), color, 12, 8);
        void Eye(Vector3 center, float radius, float thickness, float tilt = 0)
        {
            var ring = new Geometry(); ring.Ring(Vector3.Zero, radius, radius + thickness, .5f, color, 24);
            ring.Transform(p => Vector3.Transform(new(p.X, p.Z, -p.Y), Matrix4x4.CreateRotationZ(tilt)), center + new Vector3(0, 0, .25f));
            g.Append(ring);
        }
        g.Box(new(0, .45f, 0), new(3.8f, .9f, 3.8f), plinth);
        switch (style)
        {
            case 0: // Faceless figure with long arms.
                B(0, 3.1f, 0, 1.3f, 4.4f, 1); B(0, 5.9f, 0, 1.9f, 1.2f, 1.5f, .35f);
                B(0, 4.6f, 0, 7, .45f, .65f, .3f); break;
            case 1: // Empty eye on a neck.
                C(0, .9f, 0, .4f, .4f, 2.5f); Eye(new(0, 5, 0), 1.6f, .55f); break;
            case 2: // Twisted stack.
                for (int i = 0; i < 5; i++) B(MathF.Sin(i * 1.4f) * .55f, 1.575f + i * 1.35f, 0,
                    2.6f - i * .3f, 1.35f, 1.8f, i * .55f);
                break;
            case 3: // Three-pronged fork.
                B(0, 2.2f, 0, .8f, 2.6f, .8f); B(0, 3.5f, 0, 4.8f, .65f, .8f);
                for (int i = -1; i <= 1; i++) B(i * 2, 4.8f + (i == 0 ? .5f : 0), 0, .6f, 2.7f + (i == 0 ? 1 : 0), .7f);
                break;
            case 4: // Three unequal heads.
                for (int i = -1; i <= 1; i++) { C(i * 1.2f, .9f, 0, .23f, .23f, 3.2f + i); Ball(i * 1.2f, 4.7f + i, 0, .8f); }
                break;
            case 5: // Crossed arches.
                B(0, 1.8f, 0, 1, 1.8f, 1); g.Arch(new(0, 3.3f, 0), 1.65f, .55f, .65f, color);
                g.Arch(new(0, 3.3f, 0), 1.65f, .55f, .65f, color, true);
                B(-1.9f, 2.1f, 0, .55f, 2.4f, .65f); B(1.9f, 2.1f, 0, .55f, 2.4f, .65f);
                B(0, 2.1f, -1.9f, .65f, 2.4f, .55f); B(0, 2.1f, 1.9f, .65f, 2.4f, .55f); break;
            case 6: // Leaning segmented obelisk.
                for (int i = 0; i < 6; i++) B(i * .23f - .55f, 1.4f + i, 0, 1.9f - i * .2f, 1, 1.3f, .15f);
                break;
            case 7: // Split tablet.
                B(-.95f, 3.3f, 0, 1.5f, 4.8f, .75f, -.18f); B(.95f, 3.8f, .3f, 1.4f, 5.8f, .75f, .3f);
                B(0, 1.4f, 0, 3.3f, 1, 1.6f); break;
            case 8: // Spiral stair to nowhere.
                C(0, .9f, 0, .25f, .25f, 6.2f);
                for (int i = 0; i < 12; i++) { float a = i * .65f; B(MathF.Cos(a), 1.15f + i * .45f, MathF.Sin(a), 2.1f, .3f, .65f, -a); }
                break;
            case 9: // Cube balanced on a sphere.
                Ball(0, 2, 0, 1.1f); B(0, 4.3f, 0, 2.7f, 2.7f, 2.7f, .65f); B(0, 5.9f, 0, .55f, .5f, .55f); break;
            case 10: // Crescent on a stem.
                C(0, .9f, 0, .4f, .25f, 2.8f);
                var crescent = new Geometry(); crescent.Arch(Vector3.Zero, 1.6f, .65f, .6f, color);
                crescent.Transform(p => Vector3.Transform(p, Matrix4x4.CreateRotationZ(-MathF.PI / 2)), new(0, 4.7f, 0));
                g.Append(crescent); break;
            case 11: // Seated idol.
                B(0, 1.5f, 0, 2.8f, 1.2f, 2.5f); B(0, 3.1f, -.6f, 1.8f, 2, 1.3f);
                Ball(0, 4.8f, -.6f, .85f); B(-1.2f, 3.5f, .1f, .5f, 2.8f, .6f); B(1.2f, 3.5f, .1f, .5f, 2.8f, .6f); break;
            case 12: // Misaligned square portal.
                B(-1.5f, 3.2f, 0, .6f, 4.6f, .8f); B(1.5f, 3.2f, .4f, .6f, 4.6f, .8f);
                B(0, 5.4f, .2f, 3.6f, .6f, 1.2f); B(0, 1.2f, .2f, 3.6f, .6f, 1.2f); break;
            case 13: // Skeletal bird.
                C(0, .9f, 0, .3f, .3f, 2.7f); g.Ellipsoid(new(0, 4, 0), new(.7f, 1.2f, .8f), color);
                B(0, 4.2f, 0, 6.4f, .35f, 1.3f, .15f); Ball(0, 5.1f, -.3f, .6f);
                B(0, 5, -1, .35f, .3f, 1.1f); break;
            case 14: // Empty cage.
                B(0, 1.15f, 0, 3, .5f, 3); B(0, 5.4f, 0, 3, .5f, 3);
                foreach (float x in new[] { -1.25f, 1.25f }) foreach (float z in new[] { -1.25f, 1.25f }) B(x, 3.3f, z, .3f, 4, .3f);
                Eye(new(0, 3.2f, 0), .6f, .2f); break;
            case 15: // Hourglass.
                C(0, .9f, 0, 1.65f, .35f, 2.3f); C(0, 3.2f, 0, .35f, 1.65f, 2.3f);
                C(0, 5.5f, 0, 1.8f, 1.8f, .3f); break;
            case 16: // Inverted pyramid.
                C(0, .9f, 0, .4f, .4f, 1.5f); C(0, 2.4f, 0, .4f, 2.4f, 3.4f, 4);
                Ball(0, 6.3f, 0, .55f); break;
            case 17: // Two intertwined columns.
                for (int i = 0; i < 9; i++) foreach (float sign in new[] { -1f, 1f })
                    B(sign * MathF.Cos(i * .5f), 1.25f + i * .65f, sign * MathF.Sin(i * .5f), .9f, .7f, .9f, i * .5f);
                break;
            case 18: // Giant key.
                B(0, 2.65f, 0, .5f, 3.5f, .6f); Eye(new(0, 5.5f, 0), 1.1f, .45f);
                B(.65f, 1.3f, 0, 1.6f, .45f, .6f); B(.5f, 2.1f, 0, 1.3f, .45f, .6f); break;
            case 19: // Ringed planet on a pedestal.
                C(0, .9f, 0, .6f, .4f, 1.8f); Ball(0, 4.2f, 0, 1.5f);
                Eye(new(0, 4.2f, 0), 1.85f, .3f, .55f); B(.8f, 6, 0, .3f, 1.2f, .3f); break;
        }
        return g;
    }
}
