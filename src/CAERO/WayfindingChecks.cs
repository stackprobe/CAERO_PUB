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
            {43,44,45,46,47,48,49}, {42,21,22,23,24,25,26}, {41,20,7,8,9,10,27},
            {40,19,6,1,2,11,28}, {39,18,5,4,3,12,29}, {38,17,16,15,14,13,30}, {37,36,35,34,33,32,31}
        };
        for (int z = -3; z <= 3; z++)
            for (int x = -3; x <= 3; x++)
                Check(Sector.SpiralNumber(x, z) == reference[z + 3, x + 3], "clockwise spiral: center 1, east 2, southeast 3; all 49 cells");
        BigInteger r = BigInteger.Abs(new BigInteger(long.MinValue));
        Check(Sector.SpiralNumber(long.MinValue, long.MinValue) == (2 * r + 1) * (2 * r + 1) - 2 * r,
            "full signed 64-bit coordinates without overflow");
        Check(DayNightCycle.At(.1).Sun.X > .999f && DayNightCycle.At(.1).Sun.Y > 0
            && DayNightCycle.At(299.9).Sun.X < -.999f, "sun rises east (+X), sets west (-X)");
        PrimeChecks();
        DestinationPanelChecks();
        var rotations = new HashSet<int>(); var sites = new HashSet<Vector3>();
        int skyDials = 0, splitJunctions = 0;
        var signSites = new HashSet<Vector3>(); var signNormals = new HashSet<Vector3>();
        foreach (int seed in new[] {1701, 48291, -31})
            foreach (var (x, z) in new (long, long)[] {(0,0), (1,-1), (3,0), (4,0), (6,0), (-3,4), (10,0), (-40,-87), (13,-663),
                (1_000_000_000_000,-1_000_000_000_000), (long.MinValue,long.MaxValue)})
            {
                using var s = Sector.Generate(seed, x, z);
                using var repeat = Sector.Generate(seed, x, z);
                rotations.Add(s.Rotation); sites.Add(s.CompassCenter);
                signSites.Add(s.NumberPlateCenter); signNormals.Add(s.NumberPlateNormal);
                Check(s.CompassCenter == repeat.CompassCenter && s.NumberPlateCenter == repeat.NumberPlateCenter,
                    "seeded placement survives regeneration");
                Check(s.SkyCompassCenters.SequenceEqual(repeat.SkyCompassCenters), "sky dials survive regeneration");
                if (s.SkyPorts.Count > 0 && s.SkyPorts.GroupBy(p => p.Height).All(g => g.Count() < 4))
                {
                    splitJunctions++;
                    Check(!s.SkyCompassCenters.Any(), "partial crossings at different heights have no dial");
                }
                if (x == 4 && z == 0) Check(s.SkyCompassCenters.Count() == 1, "clear four-way sky junction gets a dial");
                foreach (var center in s.SkyCompassCenters)
                {
                    skyDials++;
                    Check(center.X == 0 && center.Z == 0 && center.Y > 0, "sky dial is directly above island center");
                    Check(s.SkyPorts.Count(p => p.Height == center.Y) == 4, "all four ports meet at dial elevation");
                    Check(!s.Collision.SurfaceOverlapsBox(center + new Vector3(-2.1f, .02f, -2.1f),
                        center + new Vector3(2.1f, 2.2f, 2.1f)), "sky dial has clear headroom");
                    for (float dx = -2; dx <= 2; dx += .25f)
                        for (float dz = -2; dz <= 2; dz += .25f)
                            Check(Math.Abs(s.Collision.FloorAt(dx, dz, center.Y + .01f) - center.Y) < .001f,
                                "sky inlay has no steps or floor gaps");
                }
                foreach (var panel in s.DestinationPanels)
                {
                    Vector3 forward = s.Transform(panel.Forward) - s.Offset;
                    int dx = (int)forward.X, dz = (int)forward.Z;
                    Check(panel.Number == Sector.SpiralNumber((long)((Int128)x + dx), (long)((Int128)z + dz)),
                        "bridge floor shows the other island number in every world rotation");
                    Check(panel.Surface.Vertices.All(v => Math.Abs(s.Collision.FloorAt(s.Transform(v).X, s.Transform(v).Z,
                        panel.Center.Y + .01f) - panel.Center.Y) < .001f),
                        "every destination inlay vertex has a flush supported floor");
                    Check(Vector3.Dot(s.Transform(panel.Center) - s.Offset, forward) == 42,
                        "destination panels stay at the bridge entrance and face the destination");
                    if (panel.Center.Y > 0)
                    {
                        Check(panel.Width <= 2.401f, "sky-bridge numbers stay inside the narrow parapets");
                        if (panel.Number <= 9_999_999)
                            Check(panel.Height <= 9 * panel.DigitHeight / 5 + .001f,
                                "generated sky-bridge panels keep up to seven digits on one line");
                    }
                }
                Check(PlacementSupport.HasFlatSupport(s.Collision, s.CompassCenter, new(2)), "entire inlaid dial has floor support");
                Check(!s.Collision.SurfaceOverlapsBox(s.CompassCenter + new Vector3(-2, .03f, -2),
                    s.CompassCenter + new Vector3(2, 2, 2)), "dial remains visible and accessible after sculpture placement");
                Check(s.Collision.Obstructs(s.NumberPlateCenter + s.NumberPlateNormal,
                    s.NumberPlateCenter), "plate remains part of a solid wall");
                var approach = s.NumberPlateCenter + s.NumberPlateNormal * 1.1f;
                approach.Y = Explorer.EyeHeight;
                Check(!s.Collision.Blocked(approach), "sign has a clear approach after sculpture placement");
                var old = s.CompassCenter;
                var oldSky = s.SkyCompassCenters.ToArray();
                s.SetOrigin(x == long.MaxValue ? x - 1 : x + 1, z);
                Check(Vector3.Distance(s.CompassCenter, old - new Vector3(World.CellSize, 0, 0)) < .001f,
                    "markers follow floating origin");
                Check(s.SkyCompassCenters.SequenceEqual(oldSky.Select(p => p - new Vector3(World.CellSize, 0, 0))),
                    "sky dials follow floating origin");
            }
        Check(skyDials > 0 && splitJunctions > 0, "both complete and split sky crossings covered");
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
        Check(world.Sectors.All(s => !s.NumberWorkPending), "neighbor proofs share the existing work budget and finish too");
        var first = world.Sectors.First(s => s.X == 0 && s.Z == 0);
        Check(first.NumberIsPrime == false && first.DestinationPrimes == new Vector4(1, 0, 0, 0),
            "island 1 independently colors prime destination 2 and composite destinations 4, 6, 8");
        var second = world.Sectors.First(s => s.X == 1 && s.Z == 0);
        Check(second.NumberIsPrime == true && second.DestinationPrimes == new Vector4(1, 1, 0, 0),
            "prime island 2 independently colors destinations 11, 3, 1 and 9");
        Console.WriteLine("PASS: reference spiral, seeded wall signs, four facades, clear approaches, exact incremental prime proofs and shared frame budget.");
    }

    private static void DestinationPanelChecks()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Destination panel test failed: " + message); }
        float previousHeight = float.PositiveInfinity;
        for (int digits = 1; digits <= 39; digits++)
        {
            var panel = new DestinationPanel(BigInteger.Parse(new string('9', digits)), Vector3.Zero,
                Vector3.Normalize(new Vector3(1, 0, 1)));
            Check(panel.Width <= 3.601f && panel.Height <= 3.601f && panel.DigitHeight >= .5f,
                "full coordinate range fits a bounded panel without microscopic digits");
            Check(panel.Surface.Vertices.All(v => Math.Abs(v.X) < 2.6f && Math.Abs(v.Z) < 2.6f && v.Y == 0),
                "diagonal number panels remain wholly inside both six-wide bridge entrances");
            var narrow = new DestinationPanel(panel.Number, Vector3.Zero, Vector3.UnitZ, maxWidth: 2.4f, maxHeight: 6,
                singleLineDigits: 7);
            Check(narrow.Width <= 2.401f && narrow.Height <= 6 && narrow.DigitHeight >= .5f,
                "even 39-digit sky-bridge numbers fit inside the rails with readable digits");
            if (digits <= 7)
                Check(narrow.Height <= 9 * narrow.DigitHeight / 5 + .001f,
                    "sky-bridge destination numbers through seven digits occupy one line");
            if (digits == 8)
                Check(narrow.Height > 9 * narrow.DigitHeight / 5,
                    "longer sky-bridge destination numbers still wrap for readability");
            if (digits == 1) Check(panel.Width == panel.Height, "one-digit frame is square");
            if (digits <= 8)
            {
                Check(panel.Height <= previousHeight, "more single-line digits reduce frame height");
                previousHeight = panel.Height;
            }
            var floor = new Geometry(); floor.Box(new(0, -.5f, 0), new(6, 1, 6), Raylib_cs.Color.Gray);
            floor.Inlay(panel.Footprint, panel.Surface);
            Check(floor.HasFiniteNormals && floor.CountCoplanarOverlaps() == 0, "number panel creates no overlapping floor faces");
            var collision = new WalkableScene(); floor.AddCollisionTo(collision);
            for (float x = -2.5f; x <= 2.5f; x += .5f)
                for (float z = -2.5f; z <= 2.5f; z += .5f)
                    Check(Math.Abs(collision.FloorAt(x, z, .01f)) < .001f, "numbers and frame leave no floor gaps or steps");
        }
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
