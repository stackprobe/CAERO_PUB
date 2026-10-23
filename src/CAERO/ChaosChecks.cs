using System.Numerics;
using System.Security.Cryptography;

namespace Caero;

internal static class ChaosChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Chaos test failed: " + message); }
    public static void Layouts()
    {
        VerticalBuckets();
        FrameTowerSurfaces();
        HighRiseSurfaces();
        using (var origin = Sector.Generate(1701, 0, 0))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            // Keep the original geometry baseline independent of mesh partitioning.
            // This sorted digest was also verified against the pre-split generator.
            var vertices = origin.Stone.Vertices.Take(origin.SculptureFirst).Concat(origin.SeaStairGeometry.Vertices)
                .Concat(origin.Land.Vertices.Take(origin.TerrainVertexCount));
            foreach (var p in vertices.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z))
                foreach (float v in new[] { p.X, p.Y, p.Z }) hash.AppendData(BitConverter.GetBytes(v == 0 ? 0f : v));
            string fingerprint = Convert.ToHexString(hash.GetHashAndReset());
            Check(fingerprint == "075B3F8802A4E52EE840006AEF77D364343B121D1B4A81F9E77FD4AFF4740D41",
                "starting island's terrain/buildings/lamps/inlays match generator baseline: " + fingerprint);
            Check(origin.ChaosTier == 0 && origin.SkyPorts.Count == 0 && origin.HighRoof == 0, "no high-rise additions on starting island");
        }
        float previous = 0; var styles = new HashSet<int>();
        foreach (int distance in new[] { 1, 3, 6, 10, 15 })
        {
            using var s = Sector.Generate(1701, distance, 0);
            Check(s.HighRoof > previous, "highrise height increases through distance bands"); previous = s.HighRoof;
            Check(s.Stone.HasFiniteNormals && s.Stone.Vertices.All(p => float.IsFinite(p.Y)), "finite chaos geometry");
            Check((s.FloatingStructureCount == 0) == (s.ChaosTier < 3), "intentional suspended structures only in outer bands");
            var center = s.Offset;
            Check(s.Stone.Vertices.All(p => Math.Abs(p.X - center.X) <= 96.01f && Math.Abs(p.Z - center.Z) <= 96.01f), "structures stay inside collision ownership cells");
            foreach (var p in s.SkyPorts)
            {
                Check(p.Height == Sector.SkyHeightAt(s.X + p.Dx, s.Z + p.Dz, -p.Dx, -p.Dz), "adjacent sky port heights match");
                Check(Math.Abs(s.Collision.FloorAt(p.End.X, p.End.Z, p.Height + .02f) - p.Height) < .02f, "sky port has floor at boundary");
            }
            styles.Add(s.SkylineStyle);
        }
        foreach (int seed in Enumerable.Range(1, 12))
        {
            using var s = Sector.Generate(seed, 8, -4); styles.Add(s.SkylineStyle);
        }
        Check(styles.Count == 4, "all skyline styles represented");
        using var repeatA = Sector.Generate(1701, 10, 0);
        using var repeatB = Sector.Generate(1701, 10, 0);
        Check(repeatA.Stone.Vertices.SequenceEqual(repeatB.Stone.Vertices), "chaotic structures regenerate deterministically");
    }
    private static void FrameTowerSurfaces()
    {
        var rotations = new HashSet<int>(); int towers = 0;
        var cases = new[] { (1701, 1, -32), (1701, 3, 0), (1701, 6, 0), (1701, 10, 0), (1701, 100, -100) }
            .Concat(Enumerable.Range(1, 16).Select(seed => (seed, 12, -4)));
        foreach (var (seed, x, z) in cases)
        {
            using var s = Sector.Generate(seed, x, z);
            foreach (var tower in s.FrameTowers)
            {
                towers++; rotations.Add(s.Rotation);
                int overlaps = s.Stone.CountCoplanarOverlaps(tower.First, tower.Count);
                Check(overlaps == 0, $"frame tower floor/pillar overlap: seed={seed}, island={x},{z}, count={overlaps}");
                for (float y = 12; y <= tower.Height; y += 18)
                    foreach (var local in new[] { new Vector3(tower.X, y + .7f, tower.Z),
                        new(tower.X - tower.Width / 2 + 1.1f, y + .7f, tower.Z - tower.Depth / 2 + 1.1f),
                        new(tower.X + tower.Width / 2 - 1.1f, y + .7f, tower.Z + tower.Depth / 2 - 1.1f) })
                    {
                        var p = s.Transform(local);
                        float support = s.Collision.FloorAt(p.X, p.Z, p.Y + .02f);
                        Check(Math.Abs(support - p.Y) < .02f, $"frame floor support: seed={seed}, island={x},{z}, local={local}, floor={support}, expected={p.Y}");
                    }
            }
        }
        Check(towers > 12 && rotations.Count == 4, $"frame tower audit covers multiple seeds, tiers and all rotations: towers={towers}, rotations={rotations.Count}");
        Console.WriteLine($"FRAME TOWERS: {towers} floor/pillar surface audits and floor supports passed.");
    }
    private static void HighRiseSurfaces()
    {
        var styles = new HashSet<int>(); var rotations = new HashSet<int>();
        foreach (var (seed, x, z) in new[] { (1701, 10, 16), (1701, 1, 0), (1701, 3, 0), (1701, 6, 0), (1701, 100, -100),
            (1, 8, -4), (2, 8, -4), (3, 8, -4), (4, 8, -4), (5, 8, -4), (6, 8, -4), (7, 8, -4), (8, 8, -4),
            (9, 8, -4), (10, 8, -4), (11, 8, -4), (12, 8, -4) })
        {
            using var s = Sector.Generate(seed, x, z);
            styles.Add(s.SkylineStyle); rotations.Add(s.Rotation);
            int overlaps = s.CountHighRiseOverlaps();
            Check(overlaps == 0, $"tower/beam/corridor surface overlap: seed={seed}, island={x},{z}, count={overlaps}");
            foreach (float px in new[] { -30.8f, -30.6f, -26, -22, -14, -13.2f })
                foreach (float pz in new[] { 11.1f, 11.4f, 12, 20, 32.6f, 32.9f })
                {
                    var p = s.Transform(new(px, s.HighRoof, pz));
                    Check(Math.Abs(s.Collision.FloorAt(p.X, p.Z, p.Y + .02f) - p.Y) < .02f, "roof corners remain supported after partition");
                }
        }
        Check(styles.Count == 4 && rotations.Count == 4, "surface audit covers all tower styles and rotations");
    }
    private static void VerticalBuckets()
    {
        var mesh = new Geometry();
        var color = Raylib_cs.Color.Gray;
        mesh.Quad(new(-32, 0, -4), new(-32, 0, 4), new(32, 64, 4), new(32, 64, -4), color);
        mesh.Box(new(0, 39.5f, 0), new(4, 1, 4), color);
        mesh.Quad(new(-32, 80, -4), new(32, 112, -4), new(32, 112, 4), new(-32, 80, 4), color);
        mesh.Quad(new(-2, 90, -2), new(2, 90, -2), new(2, 90, 2), new(-2, 90, 2), color);
        var scene = new WalkableScene(); mesh.AddCollisionTo(scene);
        Check(Math.Abs(scene.FloorAt(0, 0, 60) - 40) < .01f, "vertical floor search does not stop at a lower sloped hit");
        Check(Math.Abs(scene.CeilingAt(0, 0, 81) - 90) < .01f, "vertical ceiling search does not stop at a higher sloped hit");
        Check(Math.Abs(scene.FloorAt(0, 0, 35) - 32) < .01f, "sloped floor across vertical buckets");
    }
    public static void Jumps(World world)
    {
        var s = world.Sectors.First(s => s.X == 0 && s.Z == 0);
        var rail = new Explorer { Position = s.Transform(new(0, Explorer.EyeHeight, 55)) };
        Vector3 across = Vector3.Normalize(s.Transform(Vector3.UnitX) - s.Transform(Vector3.Zero));
        rail.Advance(.01f, world, across, true, true, false, true);
        for (int i = 0; i < 28; i++) rail.Advance(.01f, world, across, false, false, false, true);
        for (int i = 0; i < 330; i++) rail.Advance(.01f, world, Vector3.Zero, false, false, false, false);
        Check(Math.Abs(rail.Position.Y - Explorer.EyeHeight - 1.3f) < .05f && !world.Blocked(rail.Position), "jump onto bridge rail and stand on it");
        foreach (float dt in new[] { .01f, .05f, .1f })
        {
            var jumper = new Explorer { Position = s.Transform(new(5, Explorer.EyeHeight, 4)) };
            float initialY = jumper.Position.Y, peak = initialY;
            jumper.Advance(dt, world, Vector3.Zero, true, true, false, false);
            peak = Math.Max(peak, jumper.Position.Y);
            for (int i = 0; i < (int)(2 / dt); i++)
            {
                jumper.Advance(dt, world, Vector3.Zero, true, false, false, false);
                peak = Math.Max(peak, jumper.Position.Y);
            }
            // The old initial speed was 6 with gravity 18: its ideal apex was 1 unit.
            Check(Math.Abs((peak - initialY) - 1.5f) < .06f, $"jump apex is 1.5x original at dt={dt}: {peak - initialY}");
            Check(Math.Abs(jumper.Position.Y - initialY) < .02f, "jump lands and holding Space does not jump again");
        }
    }
    public static int SkySeams(World world)
    {
        world.Teleport(new Explorer(), new(6, 0, Vector3.Zero));
        int count = 0;
        var paths = new List<(string Name, WorldPosition[] Points)>();
        foreach (var s in world.Sectors.GroupBy(s => s.SkylineStyle).Select(g => g.First()).ToArray())
        {
            var stairs = s.Routes.First(r => r.Name == "highrise switchbacks").Points.Take(7).ToArray();
            paths.Add(("highrise first floor", stairs.Select(world.AddressOf).ToArray()));
            var roof = s.Routes.First(r => r.Name == "old roof to highrise").Points;
            paths.Add(("roof crossing", roof.Select(world.AddressOf).ToArray()));
        }
        var center = world.Sectors.First(s => s.X == 6 && s.Z == 0);
        foreach (var port in center.SkyPorts)
        {
            Vector3 d = new(port.Dx, 0, port.Dz);
            Vector3[] seam = [port.End - d * 4, port.End + d * 4];
            paths.Add(("neighbor sky seam", seam.Select(world.AddressOf).ToArray()));
        }
        foreach (var path in paths)
        {
            ProceduralChecks.Walk(world, path.Name, path.Points);
            ProceduralChecks.Walk(world, path.Name + " reverse", path.Points.Reverse().ToArray()); count += 2;
        }
        return count;
    }
}
