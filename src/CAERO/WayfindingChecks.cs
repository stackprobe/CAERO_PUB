using System.Numerics;

namespace Caero;

internal static class WayfindingChecks
{
    public static void Run()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Wayfinding test failed: " + message); }
        var floor = new Geometry();
        floor.Box(new(0, -.5f, 0), new(8, 1, 8), new(160, 160, 160, 240));
        var patch = new Geometry();
        patch.Quad(new(-1, 0, 1), new(1, 0, 1), new(1, 0, -1), new(-1, 0, -1), new(200, 170, 100, 253));
        floor.Inlay(patch, patch);
        Check(floor.CountCoplanarOverlaps() == 0 && floor.HasFiniteNormals, "inlay replaces floor with no coplanar overlap");
        var collision = new WalkableScene(); floor.AddCollisionTo(collision);
        for (float x = -2; x <= 2; x += .125f)
            for (float z = -2; z <= 2; z += .125f)
                Check(collision.FloorAt(x, z, .01f) == 0, "clipped floor stays flat and watertight across patch boundaries");
        int[,] reference = {
            {37,36,35,34,33,32,31}, {38,17,16,15,14,13,30}, {39,18,5,4,3,12,29},
            {40,19,6,1,2,11,28}, {41,20,7,8,9,10,27}, {42,21,22,23,24,25,26}, {43,44,45,46,47,48,49}
        };
        for (int z = -3; z <= 3; z++)
            for (int x = -3; x <= 3; x++)
                Check(Sector.SpiralNumber(x, z) == reference[z + 3, x + 3], "matches all 49 cells in supplied spiral");
        BigInteger r = BigInteger.Abs(new BigInteger(long.MinValue));
        Check(Sector.SpiralNumber(long.MinValue, long.MinValue) == (2 * r + 1) * (2 * r + 1) - 4 * r,
            "full signed 64-bit coordinates without overflow");
        Check(DayNightCycle.At(.1).Sun.X > .999f && DayNightCycle.At(.1).Sun.Y > 0
            && DayNightCycle.At(299.9).Sun.X < -.999f, "sun rises east (+X), sets west (-X)");
        PrimeChecks();
        var rotations = new HashSet<int>(); var sites = new HashSet<Vector3>();
        var signSites = new HashSet<Vector3>(); var signNormals = new HashSet<Vector3>();
        foreach (int seed in new[] {1701, 48291, -31})
            foreach (var (x, z) in new (long, long)[] {(0,0), (1,-1), (-3,4), (10,0), (-40,-87),
                (1_000_000_000_000,-1_000_000_000_000), (long.MinValue,long.MaxValue)})
            {
                using var s = Sector.Generate(seed, x, z);
                using var repeat = Sector.Generate(seed, x, z);
                rotations.Add(s.Rotation); sites.Add(s.CompassCenter);
                signSites.Add(s.NumberPlateCenter); signNormals.Add(s.NumberPlateNormal);
                Check(s.CompassCenter == repeat.CompassCenter && s.NumberPlateCenter == repeat.NumberPlateCenter,
                    "seeded placement survives regeneration");
                Check(PlacementSupport.HasFlatSupport(s.Collision, s.CompassCenter, new(2)), "entire inlaid dial has floor support");
                Check(!s.Collision.SurfaceOverlapsBox(s.CompassCenter + new Vector3(-2, .03f, -2),
                    s.CompassCenter + new Vector3(2, 2, 2)), "dial remains visible and accessible after sculpture placement");
                Check(s.Collision.Obstructs(s.NumberPlateCenter + s.NumberPlateNormal,
                    s.NumberPlateCenter), "plate remains part of a solid wall");
                var approach = s.NumberPlateCenter + s.NumberPlateNormal * 1.1f;
                approach.Y = Explorer.EyeHeight;
                Check(!s.Collision.Blocked(approach), "sign has a clear approach after sculpture placement");
                var old = s.CompassCenter;
                s.SetOrigin(x == long.MaxValue ? x - 1 : x + 1, z);
                Check(Vector3.Distance(s.CompassCenter, old - new Vector3(World.CellSize, 0, 0)) < .001f,
                    "markers follow floating origin");
            }
        Check(rotations.Count == 4 && sites.Count > 10, "all rotations and varied sites covered");
        for (int seed = 0; seed < 24; seed++)
        {
            using var s = Sector.Generate(seed, 1, 0);
            signSites.Add(s.NumberPlateCenter); signNormals.Add(s.NumberPlateNormal);
        }
        Check(signSites.Count > 15 && signNormals.Count == 4, $"seeded sign placement varies across wall faces: {signSites.Count} sites, {signNormals.Count} normals");
        using var world = new World(upload:false);
        for (int frame = 0; frame < 200; frame++)
        {
            world.Update(Vector3.Zero);
            Check(world.LastPrimeWorkSteps <= 64, "all islands share the per-frame arithmetic limit");
        }
        Check(world.Sectors.All(s => s.NumberIsPrime.HasValue), "small island numbers finish over successive frames");
        Console.WriteLine("PASS: reference spiral, seeded wall signs, four facades, clear approaches, exact incremental prime proofs and shared frame budget.");
    }

    private static void PrimeChecks()
    {
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Prime test failed: " + message); }
        for (int n = 0; n <= 500; n++)
        {
            bool expected = n >= 2;
            for (int d = 2; d * d <= n; d++) if (n % d == 0) expected = false;
            using var proof = new IncrementalPrime(n);
            for (int step = 0; !proof.Result.HasValue && step < 20_000; step++) proof.Step();
            Check(proof.Result == expected, $"small exact result {n}");
        }
        foreach (var (text, expected) in new[] {
            ("10007", true), ("2147483647", true), ("2305843009213693951", true),
            ("170141183460469231731687303715884105727", true),
            ("3215031751", false), ("318665857834031151167461", false),
            ("4951760154835678088235319297", false) })
        {
            using var proof = new IncrementalPrime(BigInteger.Parse(text));
            if (text == "2305843009213693951")
            {
                for (int i = 0; i < 32; i++) proof.Step();
                Check(!proof.Result.HasValue, "probable-prime screening alone never confirms prime");
            }
            for (int step = 0; !proof.Result.HasValue && step < 2_000_000; step++) proof.Step();
            Check(proof.Result == expected, $"large exact proof {text}, steps={proof.Steps}");
        }
    }
}
