using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class DiagonalBridgeChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Diagonal bridge test failed: " + message); }

    internal static void SeaStairs()
    {
        int omitted = 0, kept = 0;
        var rotations = new HashSet<int>();
        foreach (var (x, z) in new[] { (541L, 485L), (0L, 0L), (2L, -2L) })
        {
            using var world = new World(upload: false, originX: x, originZ: z);
            Settle(world);
            foreach (var sector in world.Sectors)
            {
                using var original = Sector.Generate(world.Seed, sector.X, sector.Z);
                bool overlap = world.DiagonalBridges.Any(original.SeaStairOverlaps);
                Check(sector.HasSeaStair == !overlap, "only stairs overlapping a real prime bridge are omitted");
                Check(sector.Routes.Any(r => r.Name == "sea arrival") == sector.HasSeaStair,
                    "omitted stairs do not retain a walking route");
                Vector3 bottom = sector.Transform(new(-25, -20, 89));
                float floor = sector.Collision.FloorAt(bottom.X, bottom.Z, -19.99f);
                Check(sector.HasSeaStair ? Math.Abs(floor + 20) < .01f : float.IsNegativeInfinity(floor),
                    "stair collision is retained or removed together with the mesh");
                Check(sector.Collision.SphereBlocked(bottom, .1f) == sector.HasSeaStair
                    && sector.Collision.Obstructs(bottom - Vector3.UnitY, bottom + Vector3.UnitY) == sector.HasSeaStair
                    && sector.Collision.SurfaceOverlapsBox(bottom - new Vector3(.1f), bottom + new Vector3(.1f)) == sector.HasSeaStair,
                    "all collision queries respect removed stairs after origin shifts");
                if (sector.HasSeaStair) kept++; else omitted++;
                rotations.Add(sector.Rotation);
            }
            if (x == 541)
                Check(!world.Sectors.Single(s => s.X == x && s.Z == z).HasSeaStair,
                    "reported island 1169587 has no obstructed sea stair");
            else if (x == 0)
                Check(world.Sectors.Single(s => s.X == 0 && s.Z == 0).HasSeaStair, "starting island stair remains unchanged");
            Check(world.DiagonalBridges.All(b => b.JunctionOverlaps == 0), "bridge floors retain clean junctions");
            var state = world.Sectors.ToDictionary(s => (s.X, s.Z), s => s.HasSeaStair);
            world.Teleport(new Explorer(), new(2000, 2000, Vector3.Zero), radius: 0);
            world.Teleport(new Explorer(), new(x, z, Vector3.Zero)); Settle(world);
            Check(world.Sectors.All(s => state[(s.X, s.Z)] == s.HasSeaStair), "revisit reproduces the same omissions");
        }
        Check(omitted > 0 && kept > 0 && rotations.Count == 4, "both outcomes and every rotation are covered");
        Console.WriteLine($"SEA STAIRS: {omitted} overlapping stairs omitted, {kept} retained, four rotations, collision, prime decisions and revisit passed.");
    }

    internal static void CaptureSeaStair()
    {
        WindowHost.Open();
        try
        {
            using var world = new World(originX: 541, originZ: 485);
            Settle(world);
            var sector = world.Sectors.Single(s => s.X == 541 && s.Z == 485);
            Check(!sector.HasSeaStair && world.DiagonalBridges.Any(), "reported stair omitted while bridge is present");
            using var renderer = new Renderer(world, 12345);
            var player = new Explorer();
            player.GoTo(new("SEA STAIR OMITTED", sector.Transform(new(-33, 4, 48)), sector.Transform(new(-25, -3, 62))));
            for (int i = 0; i < 4; i++)
            { Raylib.BeginDrawing(); renderer.Render(world, player.Camera, 0, 150); Raylib.EndDrawing(); }
            string path = Path.Combine(SaveStore.DirectoryPath, "Captures", "sea-stair-1169587-omitted.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var image = Raylib.LoadImageFromScreen();
            try { Check(Raylib.ExportImage(image, path), "sea stair capture saved"); }
            finally { Raylib.UnloadImage(image); }
            Console.WriteLine("SEA STAIR RENDER: " + path);
        }
        finally { Raylib.CloseWindow(); }
    }

    internal static void Run()
    {
        using (var from = Sector.Generate(1701, 1, -1))
        using (var to = Sector.Generate(1701, 2, -2))
        using (var bridge = new DiagonalBridge(from, to, false))
        {
            Check(bridge.JunctionOverlapsRemoved > 0, "reported junction reproduces the original overlap");
            Check(bridge.JunctionOverlaps == 0, "reported (2,-2) junction has no coplanar faces");
        }
        using var world = new World(upload: false);
        Check(!world.DiagonalBridges.Any(), "pending primes do not create bridges");
        Settle(world);
        Check(world.DiagonalBridges.All(b => b.JunctionOverlaps == 0), "streamed junctions have no coplanar faces");
        var sectors = world.Sectors.ToArray();
        bool Prime(Sector s)
        {
            int n = (int)s.IslandNumber;
            return n >= 2 && !Enumerable.Range(2, Math.Max(0, (int)Math.Sqrt(n) - 1)).Any(d => n % d == 0);
        }
        var expected = (from a in sectors from b in sectors
                        where b.X == a.X + 1 && Math.Abs(b.Z - a.Z) == 1 && Prime(a) && Prime(b)
                        select ((a.X, a.Z), (b.X, b.Z))).ToHashSet();
        Check(expected.Count > 0 && expected.SetEquals(world.DiagonalBridges.Select(b => (b.From, b.To))),
            "exactly the diagonal pairs of primes are connected");
        foreach (var bridge in world.DiagonalBridges.ToArray())
        {
            Check(bridge.DestinationPanels[0].Number == Sector.SpiralNumber(bridge.To.X, bridge.To.Z)
                && bridge.DestinationPanels[1].Number == Sector.SpiralNumber(bridge.From.X, bridge.From.Z),
                "both diagonal entrances show the opposite island number instead of D");
            Vector3 toward = Vector3.Normalize(new Vector3(1, 0, Math.Sign(bridge.To.Z - bridge.From.Z)));
            Check(Vector3.Dot(bridge.DestinationPanels[0].Forward, toward) > .999f
                && Vector3.Dot(bridge.DestinationPanels[1].Forward, -toward) > .999f,
                "both panels face the diagonal destination rather than the detour's first segment");
            world.ReturnToStart(new Explorer());
            Settle(world);
            Vector3 offset = WorldPosition.Offset(bridge.From.X, bridge.From.Z, world.OriginX, world.OriginZ);
            var route = WalkingPath(bridge).Select(p => p + offset).ToArray();
            var addresses = route.Select(world.AddressOf).ToArray();
            ProceduralChecks.Walk(world, "prime diagonal", addresses);
            ProceduralChecks.Walk(world, "prime diagonal reverse", addresses.Reverse().ToArray());
        }
        world.ReturnToStart(new Explorer());
        Settle(world);
        int count = world.DiagonalBridges.Count();
        Settle(world);
        Check(count == world.DiagonalBridges.Count(), "updates do not duplicate bridges");
        var player = new Explorer();
        world.Teleport(player, new(1000000000000, -1000000000000, Vector3.Zero), radius: 0);
        Check(!world.DiagonalBridges.Any(), "eviction removes bridge meshes and collision");
        world.ReturnToStart(player);
        Settle(world);
        Check(expected.SetEquals(world.DiagonalBridges.Select(b => (b.From, b.To))), "revisit restores bridges");
        CheckDetours();
        Console.WriteLine($"Diagonal bridges OK: {expected.Count} prime pairs, bidirectional walking, origin shifts, eviction and revisit.");
    }

    private static Vector3[] WalkingPath(DiagonalBridge bridge)
    {
        var path = bridge.Path.ToArray();
        path[0] -= Vector3.Normalize(path[1] - path[0]);
        path[^1] += Vector3.Normalize(path[^1] - path[^2]);
        return path;
    }

    private static void CheckDetours()
    {
        int walks = 0;
        var styles = new HashSet<int>();
        var rotations = new HashSet<int>();
        foreach (var (seed, x, z) in new (int, long, long)[] {
            (1701, -10, -2), (1702, -10, -2), (42, -10, -2),
            (1701, 10, 16), (1701, 30, 30) })
        {
            using var world = new World(upload: false, seed: seed, originX: x, originZ: z, start: Vector3.Zero);
            Settle(world);
            foreach (var s in world.Sectors) { styles.Add(s.SkylineStyle); rotations.Add(s.Rotation); }
            var bridges = world.DiagonalBridges.ToArray();
            Check(bridges.Length > 0, $"far prime pairs at {x},{z}");
            foreach (var bridge in bridges)
            {
                Check(bridge.JunctionOverlaps == 0, "far bridge junction has no coplanar faces");
                world.Teleport(new Explorer(), new(x, z, Vector3.Zero));
                Settle(world);
                var offset = WorldPosition.Offset(bridge.From.X, bridge.From.Z, world.OriginX, world.OriginZ);
                var path = WalkingPath(bridge);
                // Audit the full walking width, including every bend, against
                // all loaded islands rather than just the two bridge owners.
                for (int i = 1; i < path.Length; i++)
                {
                    Vector3 direction = Vector3.Normalize(path[i] - path[i - 1]);
                    Vector3 side = new(-direction.Z, 0, direction.X);
                    int steps = (int)MathF.Ceiling(Vector3.Distance(path[i - 1], path[i]) * 2);
                    for (int step = 0; step <= steps; step++)
                        foreach (float lane in new[] { -2f, 0, 2f })
                        {
                            Vector3 p = offset + Vector3.Lerp(path[i - 1], path[i], (float)step / steps) + side * lane;
                            Check(Math.Abs(world.SurfaceBelow(p.X, p.Z, .1f)) < .01f, "detour floor has no gaps");
                            Check(!world.Blocked(p + Vector3.UnitY * Explorer.EyeHeight),
                                $"detour clearance: seed {seed}, pair {bridge.From}/{bridge.To}, point {p}");
                        }
                }
                var addresses = path.Select(p => world.AddressOf(p + offset)).ToArray();
                ProceduralChecks.Walk(world, "satellite detour", addresses);
                ProceduralChecks.Walk(world, "satellite detour reverse", addresses.Reverse().ToArray());
                walks += 2;
            }
        }
        Check(styles.Count == 4 && rotations.Count == 4, "satellite detours cover four building styles and rotations");
        Console.WriteLine($"Diagonal detours OK: {walks} far-island walks, three seeds, four styles/rotations, full-width clearance.");
    }

    private static void Settle(World world)
    {
        for (int i = 0; i < 10000 && world.Sectors.Any(s => !s.NumberIsPrime.HasValue); i++) world.Update(Vector3.Zero);
        Check(world.Sectors.All(s => s.NumberIsPrime.HasValue), "nearby primality completed");
        world.Update(Vector3.Zero);
    }
}
