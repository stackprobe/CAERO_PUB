using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class ExitRelic
{
    internal static float Brightness(double seconds) => .42f + .16f * (float)Math.Cos(seconds * Math.Tau / 6);

    internal static Geometry BuildAlcoveFloor()
    {
        var g = new Geometry();
        // At the first landing's height, bridge only the gap between the two
        // stairs: x=20..24, from the back wall (z=11) to the landing (z=30).
        g.Box(new(22, 5.65f, 20.5f), new(4, .7f, 19), new(168, 163, 143, 240));
        return g;
    }

    internal static Geometry Build(Vector3 foot, Color tint)
    {
        var g = new Geometry();
        Color stone = new(173, 190, 181, 255), rim = new(211, 224, 214, 255);
        // Alpha 254 marks only the crystal and luminous inlays for emission.
        Color luminous = new(tint.R, tint.G, tint.B, (byte)254);
        g.Cylinder(foot, .88f, .88f, .16f, stone, 24);
        g.Cylinder(foot + Vector3.UnitY * .16f, .72f, .67f, .20f, rim, 24);
        g.Ring(foot + Vector3.UnitY * .365f, .55f, .61f, .035f, luminous, 32);
        foreach (float x in new[] { -.68f, .68f })
            g.Cylinder(foot + new Vector3(x, .36f, 0), .12f, .07f, 1.7f, rim, 4);
        Vector3 top = foot + Vector3.UnitY * 2.65f, bottom = foot + Vector3.UnitY * .55f;
        Vector3[] waist = [foot + new Vector3(0, 1.45f, .30f), foot + new Vector3(.46f, 1.45f, 0),
            foot + new Vector3(0, 1.45f, -.30f), foot + new Vector3(-.46f, 1.45f, 0)];
        for (int i = 0; i < waist.Length; i++)
        {
            Vector3 a = waist[i], b = waist[(i + 1) % waist.Length];
            g.Triangle(top, a, b, luminous);
            g.Triangle(bottom, b, a, luminous);
        }
        g.Ring(foot + Vector3.UnitY * 1.50f, .56f, .62f, .045f, luminous, 32);
        g.RemoveCoplanarOverlaps();
        return g;
    }
}
