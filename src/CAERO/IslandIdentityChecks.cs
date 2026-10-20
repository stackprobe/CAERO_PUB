using System.Numerics;

namespace Caero;

internal static class IslandIdentityChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Island identity test failed: " + message); }

    public static void Run()
    {
        var materials = new HashSet<int>();
        var sculptures = new HashSet<int>();
        var rotations = new HashSet<int>();
        var layouts = new HashSet<string>();
        CheckSculptureVolumes();
        VariationChecks.Run();
        foreach (int seed in new[] { 1701, 48291, -31 })
            foreach (var (x, z) in new[] { (0, 0), (3, 0), (4, 0), (0, -4), (-3, -3), (5, 0), (0, -5), (9, 0), (10, 0), (-10, 0), (0, 12), (-4, -87), (-40, -87) })
            {
                using var s = Sector.Generate(seed, x, z);
                using var repeat = Sector.Generate(seed, x, z);
                rotations.Add(s.Rotation);
                Check(s.WallFinish == repeat.WallFinish && s.LampBases.SequenceEqual(repeat.LampBases)
                    && s.WeedRoots.SequenceEqual(repeat.WeedRoots) && s.Land.Vertices.SequenceEqual(repeat.Land.Vertices)
                    && s.Stone.Vertices.SequenceEqual(repeat.Stone.Vertices), "revisit reproduces every decoration");
                Check(s.Land.HasFiniteNormals && s.Stone.HasFiniteNormals, "finite decoration normals");
                Check(s.RejectedLampCount == 0 && s.LampBases.Count == 9, "all varied lanterns have support");
                if (s.IslandDistance < 5)
                    Check(s.WallFinish == Vector3.Zero, "wall materials still start at distance five");
                else
                {
                    materials.Add((int)s.WallFinish.X);
                }
                Check(s.WeedRoots.Count >= s.MinimumWeedCount && s.MinimumWeedCount >= 10, "minimum supported vegetation on every island");
                foreach (var root in s.WeedRoots)
                    Check(PlacementSupport.HasFlatSupport(s.Collision, root, new(.18f, .18f)), "weeds grow on supported paving");
                Check((s.WingCount > 0) == (s.IslandDistance >= 5), "wings start at distance five");
                CheckSculpture(s);
                Check(s.SculptureStyle == repeat.SculptureStyle && s.SculptureColor == repeat.SculptureColor
                    && s.SculptureYaw == repeat.SculptureYaw && s.SculptureFoot == repeat.SculptureFoot, "seeded shape/color/yaw/site regenerate");
                if (x == 0 && z == 0)
                    Check(s.SculptureStyle == SculptureCatalog.StartStyle && s.SculptureColor == -1, "unique first-island monument");
                else
                {
                    Check(s.SculptureStyle < SculptureCatalog.StyleCount, "special shape never appears on another island");
                    sculptures.Add(s.SculptureStyle);
                }
                if (seed == 1701)
                    layouts.Add(string.Join(";", s.LampBases.Select(p => p - s.Offset)));
            }
        Check(materials.Count >= 10 && sculptures.Count >= 10 && rotations.Count == 4, "material, sculpture and rotation variety");
        Check(layouts.Count == 13, "each sampled island has its own lantern layout");
        CheckSculptureDiversity();
        using var a = Sector.Generate(1701, 10, 0);
        using var b = Sector.Generate(48291, 10, 0);
        Check(!a.WeedRoots.SequenceEqual(b.WeedRoots), "world seed changes weed pattern");
        using var world = new World(upload: false, start: new(1920, 0, 0));
        var paths = world.Sectors.Where(s => s.IslandDistance >= 10 && Math.Abs(s.Z) <= 1 && s.X <= 11)
            .SelectMany(s => s.Routes.Select(r => (r.Name, Points: r.Points.Select(world.AddressOf).ToArray()))).ToArray();
        foreach (var route in paths)
            {
                ProceduralChecks.Walk(world, "identity " + route.Name, route.Points);
                ProceduralChecks.Walk(world, "identity reverse " + route.Name, route.Points.Reverse().ToArray());
            }
        Console.WriteLine("PASS: island materials, varied supported lanterns, seeded paving weeds, lateral wings, sculptures, distance thresholds, regeneration and decorated-island route walks.");
    }

    private static void CheckSculpture(Sector s)
    {
        Check(s.SculptureCount == 1 && s.SculptureStyle >= 0, $"exactly one safe sculpture found [{s.X},{s.Z}]");
        var structure = new WalkableScene();
        var vertices = s.Stone.Vertices;
        for (int i = 0; i < s.SculptureFirst; i += 3) structure.Add(vertices[i], vertices[i + 1], vertices[i + 2]);
        Check(!structure.SurfaceOverlapsBox(s.SculptureMin, s.SculptureMax), $"entire rotated sculpture clears walls/beams/stairs/lamps [{s.X},{s.Z}]");
        float radius = s.SculpturePlinthHalfSize * (Math.Abs(MathF.Cos(s.SculptureYaw)) + Math.Abs(MathF.Sin(s.SculptureYaw)));
        Check(PlacementSupport.HasFlatSupport(structure, s.SculptureFoot, new(radius)), "rotated plinth footprint supported before prop exists");
    }

    private static void CheckSculptureDiversity()
    {
        var styles = new HashSet<int>(); var colors = new HashSet<int>();
        var headings = new HashSet<int>(); var sites = new HashSet<(int, int)>();
        int shifted = 0;
        for (int seed = 0; seed < 128; seed++)
        {
            using var s = Sector.Generate(seed, 5 + seed % 12, -seed % 7);
            CheckSculpture(s);
            styles.Add(s.SculptureStyle); colors.Add(s.SculptureColor); headings.Add((int)(s.SculptureYaw * 1000));
            Vector3 relative = s.SculptureFoot - s.Offset;
            sites.Add(((int)(relative.X * 100), (int)(relative.Z * 100)));
            if (s.SculpturePlacementAttempts > 1) shifted++;
        }
        Check(styles.Count == 20 && colors.Count == 20, "all twenty shapes and twenty colors exercised");
        Check(headings.Count > 110 && sites.Count > 110 && shifted > 0, "continuous seeded yaw, varied sites and collision relocation");
        var combinations = new HashSet<(int, int, float)>();
        for (int x = 5; x < 10; x++)
            for (int z = -2; z <= 2; z++)
            {
                using var s = Sector.Generate(1701, x, z);
                CheckSculpture(s);
                combinations.Add((s.SculptureStyle, s.SculptureColor, s.SculptureYaw));
            }
        Check(combinations.Count == 25, "neighboring islands in one world have distinct sculpture combinations");
        Console.WriteLine($"SCULPTURES: 20 shapes/colors, {headings.Count} headings, {sites.Count} sites, {shifted} relocated placements; full-volume clearance passed.");
    }

    private static void CheckSculptureVolumes()
    {
        var signatures = new HashSet<string>();
        for (int style = 0; style < 20; style++)
        {
            var mesh = SculptureCatalog.Build(style, style, Raylib_cs.Color.Gray);
            Check(mesh.HasFiniteNormals && mesh.Vertices.All(v => float.IsFinite(v.X + v.Y + v.Z)), "finite catalog geometry");
            signatures.Add(string.Join(";", mesh.Vertices));
        }
        Check(signatures.Count == 20 && SculptureCatalog.Palette.Distinct().Count() == 20, "twenty distinct authored silhouettes and colors");
        var monument = SculptureCatalog.BuildStartMonument();
        Check(monument.HasFiniteNormals && monument.Vertices.All(v => float.IsFinite(v.X + v.Y + v.Z)), "finite starting monument geometry");
        Check(!signatures.Contains(string.Join(";", monument.Vertices)) && monument.Vertices.Max(v => v.Y) > 9,
            "starting monument has a unique enlarged silhouette");
        Check(SculptureCatalog.StartColor.B > SculptureCatalog.StartColor.G && SculptureCatalog.StartColor.G > SculptureCatalog.StartColor.R,
            "starting monument uses blue-grey stone");
        var geometry = new Geometry();
        geometry.Box(new(0, -.5f, 0), new(20, 1, 20), Raylib_cs.Color.Gray);
        geometry.Box(new(0, 6, 0), new(8, .3f, .3f), Raylib_cs.Color.Gray);
        var scene = new WalkableScene(); geometry.AddCollisionTo(scene);
        Check(PlacementSupport.HasFlatSupport(scene, Vector3.Zero, new(1.9f)), "foot support alone accepts low beam");
        Check(scene.SurfaceOverlapsBox(new(-2, .04f, -2), new(2, 7, 2)), "upper beam crossing sculpture is rejected");
        Check(!scene.SurfaceOverlapsBox(new(-2, .04f, 2), new(2, 7, 6)), "nearby shifted sculpture clears beam and ground contact");
        var wall = new Geometry(); wall.Box(new(3, 4, 0), new(.1f, 8, 8), Raylib_cs.Color.Gray); wall.AddCollisionTo(scene);
        Check(scene.SurfaceOverlapsBox(new(2.8f, .04f, 3), new(5, 7, 5)), "thin wall crossing rotated extremity rejected");
        var diagonal = new WalkableScene(); diagonal.Add(new(0, 1, 0), new(4, 1, 0), new(0, 1, 4));
        Check(!diagonal.SurfaceOverlapsBox(new(3, 0, 3), new(4, 2, 4)), "triangle bounding boxes alone must not reject a clear site");
        Check(diagonal.SurfaceOverlapsBox(new(.5f, 0, .5f), new(1, 2, 1)), "face crossing volume with no triangle vertex inside is detected");
    }
}
