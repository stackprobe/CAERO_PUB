using System.Diagnostics;
using System.Numerics;

namespace Caero;

internal static class ProceduralChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Procedural test failed: " + message); }
    public static void Run()
    {
        var timer = Stopwatch.StartNew();
        SeedChecks.Run();
        BuildingChecks.Run();
        FloatingOriginChecks.Run();
        SurfaceChecks.Run();
        DayNightChecks.Run();
        WayfindingChecks.Run();
        WarpChecks.Run();
        SpecialChecks.Run();
        MenuChecks.Run();
        DiagonalBridgeChecks.Run();
        ResidentChecks.Run();
        PredatorChecks.Run();
        CheckPropSupport();
        IslandIdentityChecks.Run();
        ChaosChecks.Layouts();
        using (var a = Sector.Generate(1701, -3, 4))
        using (var b = Sector.Generate(1701, -3, 4))
        using (var c = Sector.Generate(1702, -3, 4))
        {
            Check(a.Stone.Vertices.SequenceEqual(b.Stone.Vertices) && a.Land.Vertices.SequenceEqual(b.Land.Vertices), "same seed and coordinates reproduce all vertices");
            Check(!a.Land.Vertices.SequenceEqual(c.Land.Vertices), "different seed changes island");
            Check(a.Stone.HasFiniteNormals && a.Land.HasFiniteNormals, "finite surface normals");
            Check(a.Land.Vertices.Min(v => v.Y) < World.SeaFloor, "coast extends below seabed");
        }
        Check(World.Cell(new(-96.01f, 0, 95.99f)) == (-1, 0), "negative cell boundary");
        Check(World.Cell(new(-96, 0, 96)) == (0, 1), "exact cell boundary");
        int routes = 0;
        using var world = new World(upload: false);
        CheckSpeedModes(world);
        ChaosChecks.Jumps(world);
        WaterMovementChecks.Run(world);
        FlightChecks.Run();
        var representatives = world.Sectors.GroupBy(s => (s.Rotation, s.HallHeight, s.TowerFloors)).Select(g => g.First()).ToArray();
        Check(representatives.Select(s => s.Rotation).Distinct().Count() == 4, "all four orientations covered");
        var paths = representatives.SelectMany(s => s.Routes.Where(r => !r.Name.StartsWith("highrise"))
            .Select(r => (Name: r.Name + $" [{s.X},{s.Z}]", Points: r.Points.Select(world.AddressOf).ToArray()))).ToArray();
        foreach (var path in paths)
        {
            Walk(world, path.Name, path.Points);
            Walk(world, path.Name + " reverse", path.Points.Reverse().ToArray()); routes += 2;
        }
        // Cross ownership seams with the real movement controller in both axes and directions.
        foreach (var (from, to) in new (Vector3, Vector3)[] {
            (new(80, 0, 0), new(112, 0, 0)), (new(0, 0, 80), new(0, 0, 112)),
            (new(-112, 0, 0), new(-80, 0, 0)), (new(0, 0, -112), new(0, 0, -80)) })
        {
            WorldPosition[] seam = [new(0, 0, from), new(0, 0, to)];
            Walk(world, "bridge seam", seam); Walk(world, "bridge seam reverse", seam.Reverse().ToArray());
        }
        world.ReturnToStart(new Explorer());
        var first = world.Sectors.First(s => s.X == 0 && s.Z == 0);
        var center = first.Transform(new(5, 0, 4));
        var explorer = new Explorer { Position = center + Vector3.UnitY * Explorer.EyeHeight };
        explorer.Advance(.02f, world, Vector3.Zero, true, true, false, false);
        float peak = explorer.Position.Y;
        for (int i = 0; i < 200; i++)
        { explorer.Advance(.02f, world, Vector3.Zero, false, false, false, false); peak = Math.Max(peak, explorer.Position.Y); }
        Check(peak > center.Y + Explorer.EyeHeight + .7f, "jump rises");
        Check(Math.Abs(explorer.Position.Y - center.Y - Explorer.EyeHeight) < .05f, "jump lands");
        routes += ChaosChecks.SkySeams(world);
        world.Teleport(explorer, new(100, -100, Vector3.Zero), radius: 1);
        Check(world.LoadedCount <= 49, "synchronous teleport also evicts first");
        WaitForStreaming(world, explorer.Position);
        Check(world.LoadedCount <= 49 && world.Sectors.All(s => Math.Abs(s.X - 100) <= 3 && Math.Abs(s.Z + 100) <= 3), "distant render and collision sectors evicted");
        Check(!world.Sectors.Any(s => s.X == 0 && s.Z == 0), "eviction removes old collision ownership");
        var distantPaths = world.Sectors.First(s => s.X == 100 && s.Z == -100).Routes
            .Select(r => (r.Name, Points: r.Points.Select(world.AddressOf).ToArray())).ToArray();
        foreach (var route in distantPaths)
        {
            Walk(world, "distant " + route.Name, route.Points);
            Walk(world, "distant reverse " + route.Name, route.Points.Reverse().ToArray()); routes += 2;
        }
        world.ReturnToStart(explorer);
        WaitForStreaming(world, explorer.Position);
        Check(Math.Abs(world.SurfaceBelow(0, 0, 1)) < .01f, "return regenerates floor");
        using var regenerated = Sector.Generate(world.Seed, 0, 0);
        var revisited = world.Sectors.First(s => s.X == 0 && s.Z == 0);
        Check(regenerated.Routes.SelectMany(r => r.Points).SequenceEqual(revisited.Routes.SelectMany(r => r.Points)), "revisited routes identical");
        // Obsolete in-flight work after a teleport cannot grow memory without bound.
        for (int i = 0; i < 12; i++)
        { world.Teleport(explorer, new(15 + i, 15, Vector3.Zero), radius: 0); world.Update(explorer.Position); }
        world.ReturnToStart(explorer);
        WaitForStreaming(world, Vector3.Zero);
        Check(world.LoadedCount <= 49, "teleport churn stays bounded");
        SaveRoundTrip(world);
        string result = $"PASS: 128-bit SHA-512 coordinate seeds and xor128, impact-dependent sinking and passive buoyancy, distance-based chaos, starting island with compass/number inlays, skyline variants, matching sky ports, 1.5x original jump apex and rail landing, grounded lamps, deterministic islands, {routes} generated route walks, seams, streaming eviction/regeneration, teleport churn, seed/position save and legacy migration. {timer.Elapsed.TotalSeconds:F2}s";
        Console.WriteLine(result);
        Directory.CreateDirectory(SaveStore.DirectoryPath);
        File.WriteAllText(Path.Combine(SaveStore.DirectoryPath, "stream-test.txt"), result + "\n");
    }
    private static void CheckPropSupport()
    {
        // Reproduce the reported island and cover rotations, heights, seeds and far coordinates.
        var rotations = new HashSet<int>();
        foreach (int seed in new[] { 1701, 48291, -31 })
            foreach (var (x, z) in new[] { (5, 5), (0, 0), (-3, 4), (100, -100), (-156, 156), (2, -1) })
            {
                using var s = Sector.Generate(seed, x, z); rotations.Add(s.Rotation);
                Check(s.RejectedLampCount == 0 && s.LampBases.Count == 9 && s.Lights.Count == 9,
                    $"all intended lamps have structural support: seed {seed}, island {x},{z}");
                foreach (var foot in s.LampBases)
                    Check(PlacementSupport.HasFlatSupport(s.Collision, foot, new(.4f, .4f)), "lamp footprint is fully supported after rotation");
            }
        Check(rotations.Count == 4, "lamp support covers every rotation");
        var bridge = new Geometry();
        bridge.Box(new(0, -.35f, 66), new(8, .7f, 58), Raylib_cs.Color.Gray);
        var support = new WalkableScene(); bridge.AddCollisionTo(support);
        Check(PlacementSupport.HasFlatSupport(support, new(2.8f, 0, 66), new(.4f, .4f)), "bridge lamp fits inside deck");
        Check(!PlacementSupport.HasFlatSupport(support, new(6, 0, 66), new(.4f, .4f)), "old floating lamp position rejected");
        Check(!PlacementSupport.HasFlatSupport(support, new(3.8f, 0, 66), new(.4f, .4f)), "partially overhanging pedestal rejected");
        Check(!PlacementSupport.HasFlatSupport(support, new(0, 1, 66), new(.4f, .4f)), "lamp above an existing floor rejected");
        Check(!PlacementSupport.HasFlatSupport(support, new(0, 0, 100), new(.4f, .4f)), "bridge end overhang rejected");
    }
    private static void CheckSpeedModes(World world)
    {
        var s = world.Sectors.First(s => s.X == 0 && s.Z == 0);
        Vector3 along = Vector3.Normalize(s.Transform(Vector3.UnitZ) - s.Transform(Vector3.Zero));
        foreach (bool flying in new[] { false, true })
            foreach (var (fast, speed) in new[] { (false, 5f), (true, 13f) })
            {
                Vector3 start = s.Transform(new(0, flying ? 80 : Explorer.EyeHeight, 50));
                var e = new Explorer { Position = start, Flying = flying };
                e.Advance(.1f, world, along, false, false, false, fast);
                Check(Math.Abs(Vector3.Dot(e.Position - start, along) - speed * .1f) < .002f,
                    $"walk/fly speed: flying={flying}, fast={fast}");
            }
        foreach (bool fast in new[] { false, true })
        {
            var e = new Explorer { Position = new(80, -25, 80) };
            e.Advance(.1f, world, Vector3.UnitX, false, false, false, fast);
            Check(e.Swimming && Math.Abs(e.Position.X - 80 - (fast ? .9f : .4f)) < .002f, "normal/Shift swimming speed");
        }
        foreach (float dt in new[] { .01f, .05f, .1f })
        {
            Vector3 start = s.Transform(new(8, Explorer.EyeHeight, 22));
            Vector3 across = Vector3.Normalize(s.Transform(Vector3.UnitX) - s.Transform(Vector3.Zero));
            var e = new Explorer { Position = start };
            for (int i = 0; i < (int)(.4f / dt); i++)
                e.Advance(dt, world, across, false, false, false, true);
            Check(Vector3.Dot(e.Position - start, across) < 4 && !world.Blocked(e.Position), "Shift movement cannot tunnel through tower wall");
        }
        Console.WriteLine("SPEED: normal/Shift walking, flying, swimming and wall collision passed.");
    }
    private static void WaitForStreaming(World world, Vector3 p)
    {
        var timeout = Stopwatch.StartNew(); var cell = world.GlobalCell(p);
        do
        {
            world.Update(p);
            if (world.Sectors.Count(s => Math.Abs(s.X - cell.X) <= 2 && Math.Abs(s.Z - cell.Z) <= 2) == 25 && !world.Generating) return;
            Thread.Sleep(1);
        } while (timeout.Elapsed.TotalSeconds < 30);
        throw new InvalidOperationException("Streaming timed out.");
    }
    internal static void Walk(World world, string name, Vector3[] points)
        => Walk(world, name, points.Select(world.AddressOf).ToArray());
    internal static void Walk(World world, string name, WorldPosition[] points)
    {
        var explorer = new Explorer();
        world.Teleport(explorer, points[0] with { Local = points[0].Local + Vector3.UnitY * Explorer.EyeHeight });
        // These fixed centerline routes audit architecture without steering.
        // ResidentChecks exercises body contact and passing on both sides.
        foreach (var sector in world.Sectors) sector.People.Clear();
        for (int i = 1; i < points.Length; i++)
        {
            var target = world.LocalOf(points[i]);
            int limit = (int)(Vector3.Distance(world.LocalOf(points[i - 1]), target) / .15f) + 120;
            for (int step = 0; step < limit; step++)
            {
                target = world.LocalOf(points[i]);
                Vector3 delta = target - (explorer.Position - Vector3.UnitY * Explorer.EyeHeight); delta.Y = 0;
                float distance = delta.Length();
                if (distance < .12f) break;
                explorer.Advance(Math.Min(.025f, distance / 13), world, Vector3.Normalize(delta), false, false, false, true);
            }
            for (int k = 0; k < 15; k++) explorer.Advance(.02f, world, Vector3.Zero, false, false, false, false);
            target = world.LocalOf(points[i]);
            Vector3 actual = explorer.Position - Vector3.UnitY * Explorer.EyeHeight;
            Check(Vector2.Distance(new(actual.X, actual.Z), new(target.X, target.Z)) < .2f &&
                (target.Y < World.WaterLevel || Math.Abs(actual.Y - target.Y) < .5f),
                $"{name} point {i}, expected {target}, got {actual}");
            Check(!explorer.Flying, name + " requires no flight or jumping");
        }
    }
    private static void SaveRoundTrip(World world)
    {
        int seed = world.Seed;
        string path = Path.Combine(SaveStore.DirectoryPath, "generated-view.json");
        byte[]? backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            var explorer = new Explorer { Position = new(-12000, 28, 14000), Yaw = .7f, Pitch = -.2f, Flying = true };
            var expected = world.AddressOf(explorer.Position);
            Check(SaveStore.Save(explorer, world), "save succeeds");
            var view = SaveStore.Load();
            Check(view is { Y: 28, Flying: true, CoordinateVersion: 1 } && view.Seed == seed && view.GeneratorVersion == SaveStore.CurrentGeneratorVersion
                && view.IslandX == expected.IslandX && view.IslandZ == expected.IslandZ && new Vector3(view.X, view.Y, view.Z) == expected.Local,
                "seed and island/local view round trip");
            foreach (int version in new[] { 1, 2 })
            {
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new SavedView(-12000, 28, 14000, .7f, -.2f, true, 48291, version)));
                var migrated = SaveStore.Load();
                var spawn = Sector.SpawnView(48291);
                Vector3 direction = Vector3.Normalize(spawn.Target - spawn.Position);
                Check(migrated is { Seed: 48291, Flying: false } && migrated.GeneratorVersion == SaveStore.CurrentGeneratorVersion
                    && new Vector3(migrated.X, migrated.Y, migrated.Z) == spawn.Position
                    && Math.Abs(migrated.Yaw - MathF.Atan2(direction.X, -direction.Z)) < .0001f
                    && Math.Abs(migrated.Pitch - MathF.Asin(direction.Y)) < .0001f,
                    "legacy save keeps seed and restarts on the new first island");
            }
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new SavedView(0, 2, 0, 0, 0, false, seed, 999)));
            Check(SaveStore.Load() == null, "unknown future generation version rejected");
        }
        finally
        {
            if (backup != null) File.WriteAllBytes(path, backup);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}
