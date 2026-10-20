using System.Numerics;

namespace Caero;

internal static class DiagonalBridgeChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Diagonal bridge test failed: " + message); }

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
            if (seed == 1701 && x == -10 && z == -2)
                Check(Enumerable.Range(0, 131).Any(i => world.Blocked(new(38 + i, Explorer.EyeHeight, -24 - i))),
                    "reported building blocks the old straight bridge");
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
