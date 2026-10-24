using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class HallRoofChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Hall roof test failed: " + message); }

    internal static void Run()
    {
        var variants = new HashSet<(int, float)>();
        var cells = (from x in Enumerable.Range(-4, 9) from z in Enumerable.Range(-4, 9)
                     select ((long)x, (long)z)).Append((13L, 3L));
        foreach (var (x, z) in cells)
        {
            using var sector = Sector.Generate(1701, x, z);
            variants.Add((sector.Rotation, sector.HallHeight));
            var roof = new Geometry();
            var vertices = sector.Stone.Vertices;
            for (int i = 0; i < vertices.Count; i += 3)
            {
                Vector3 a = vertices[i], b = vertices[i + 1], c = vertices[i + 2];
                if (a.Y == sector.HallHeight && b.Y == a.Y && c.Y == a.Y
                    && Vector3.Cross(b - a, c - a).Y > 0)
                    roof.Triangle(a, b, c, Color.White);
            }
            Check(roof.CountCoplanarOverlaps() == 0, $"duplicate upward faces at island {x},{z}");
            // Sample the strips where column caps meet the slab, including both
            // sides of the parapets and the stair landing at every roof height.
            foreach (float px in new[] { -29.8f, -29.4f, -20f, -10f, 0f, 10f, 19.8f, 26.2f, 29.4f, 29.8f })
                foreach (float pz in new[] { -35.8f, -35.4f, -22f, -8.6f, -8.2f })
                {
                    Vector3 p = sector.Transform(new(px, sector.HallHeight, pz));
                    Check(Math.Abs(sector.Collision.FloorAt(p.X, p.Z, p.Y + .01f) - p.Y) < .001f,
                        $"roof remains supported at {x},{z}: {px},{pz}");
                }
        }
        Check(variants.Count == 12, "all four rotations and three roof heights covered");
        Console.WriteLine("HALL ROOFS: 82 islands including saved island 13,3; all rotations/heights, no duplicate roof faces and supported parapet strips passed.");
    }

    internal static void Capture()
    {
        var saved = SaveStore.Load() ?? throw new InvalidOperationException("A saved viewpoint is required.");
        WindowHost.Open();
        try
        {
            using var world = new World(seed: saved.Seed, start: new(saved.X, saved.Y, saved.Z),
                originX: saved.IslandX, originZ: saved.IslandZ);
            using var renderer = new Renderer(world, 12345);
            var explorer = new Explorer(); explorer.Restore(saved);
            string folder = Path.Combine(SaveStore.DirectoryPath, "Captures"); Directory.CreateDirectory(folder);
            foreach (var (name, time) in new[] { ("day", 150d), ("night", 450d) })
            {
                for (int frame = 0; frame < 4; frame++)
                {
                    Raylib.BeginDrawing(); renderer.Render(world, explorer.Camera, 12, time); Raylib.EndDrawing();
                }
                var image = Raylib.LoadImageFromScreen();
                try { Check(Raylib.ExportImage(image, Path.Combine(folder, $"hall-roof-{name}.png")), "capture export"); }
                finally { Raylib.UnloadImage(image); }
            }
            Console.WriteLine("HALL ROOF CAPTURES: " + folder);
        }
        finally { Raylib.CloseWindow(); }
    }
}
