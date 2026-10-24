using System.Numerics;
using System.Globalization;
using Raylib_cs;

namespace Caero;

internal sealed record WalkingRoute(string Name, Vector3[] Points);

internal sealed partial class Sector : IDisposable
{
    public long X { get; }
    public long Z { get; }
    public Vector3 Offset { get; private set; }
    public int Rotation { get; }
    public float HallHeight { get; }
    public int TowerFloors { get; }
    // Local facade order: south, north, west, east. Use a separate random stream
    // so choosing doors cannot change terrain, architecture heights or lamps.
    internal int TowerEntranceMask { get; }
    public WalkableScene Collision { get; } = new();
    public List<SceneChunk> Chunks { get; } = [];
    public List<NightLight> Lights { get; } = [];
    public List<Resident> People { get; private set; } = [];
    public List<Vector3> LampBases { get; } = [];
    public int RejectedLampCount { get; private set; }
    private readonly List<Vector3> lampRequests = [];
    public List<WalkingRoute> Routes { get; } = [];
    internal Geometry Stone { get; } = new();
    internal Geometry Land { get; } = new();
    // Keep the sea stair's final tread after GPU upload so a late prime bridge
    // can subtract its shared junction faces without rebuilding the island.
    internal Geometry BridgeJunctionSurface { get; } = new();
    internal int TerrainVertexCount { get; private set; }
    private readonly Xor128 random;
    private readonly int worldSeed;
    private readonly Color stone, pale, floor, rock;
    public Vector3 Transform(Vector3 p) => Rotate(p) + Offset;
    private Vector3 Rotate(Vector3 p) => Rotation switch {
        1 => new(-p.Z, p.Y, p.X), 2 => new(-p.X, p.Y, -p.Z), 3 => new(p.Z, p.Y, -p.X), _ => p };
    // Invariant ASCII island indices, not the player's floating-point world position.
    internal static Seed128 CoordinateSeedAt(long x, long z)
    {
        string coordinate = x.ToString(CultureInfo.InvariantCulture) + ":" + z.ToString(CultureInfo.InvariantCulture);
        return Seed128.Hash(coordinate);
    }
    internal static Seed128 SeedAt(int seed, long x, long z) => CoordinateSeedAt(x, z).WithWorldSeed(seed);
    // Scalar identifiers/effect parameters are outputs, never inputs to map PRNGs.
    internal static int ValueAt(int seed, long x, long z) => unchecked((int)new Xor128(SeedAt(seed, x, z)).NextUInt32());
    private Sector(int seed, long x, long z)
    {
        X = x; Z = z; worldSeed = seed; random = new(SeedAt(seed, x, z));
        Rotation = random.Next(4); HallHeight = 12 + random.Next(3) * 1.5f; TowerFloors = random.Next(2, 4);
        int shade = random.Next(-12, 13);
        stone = new(174 + shade, 164 + shade, 141 + shade, 220);
        pale = new(198, 188, 160, 220); floor = new(168, 163, 143, 240); rock = new(98, 113, 106, 150);
        var entrances = new Xor128(SeedAt(seed ^ 0x444f4f52, x, z));
        int[] sides = [0, 1, 2, 3];
        int count = entrances.Next(1, 5);
        for (int i = 0; i < count; i++)
        {
            int pick = entrances.Next(i, sides.Length);
            (sides[i], sides[pick]) = (sides[pick], sides[i]);
            TowerEntranceMask |= 1 << sides[i];
        }
    }
    public static Viewpoint SpawnView(int seed)
    {
        using var layout = new Sector(seed, 0, 0);
        return new("THE FIRST ISLAND", layout.Transform(new(0, Explorer.EyeHeight, 6)), layout.Transform(new(0, 8, -28)));
    }
    public static Sector Generate(int seed, long x, long z, bool includeSpecial = true)
    {
        var s = new Sector(seed, x, z);
        s.BuildIsland(); s.TerrainVertexCount = s.Land.VertexCount;
        s.BuildNetwork(); s.BuildHall(); s.BuildTower(); s.BuildSeaStair();
        s.BuildChaos();
        s.BuildIslandIdentity();
        s.PlaceLamps();
        s.BuildWayfinding();
        s.BuildWarpZone();
        s.BuildSculpture();
        // Keep vertices and collision buckets island-local, even at huge addresses.
        s.Stone.Transform(s.Rotate, Vector3.Zero);
        s.SeaStairGeometry.Transform(s.Rotate, Vector3.Zero);
        s.Land.Transform(s.Rotate, Vector3.Zero);
        s.BridgeJunctionSurface.Transform(s.Rotate, Vector3.Zero);
        s.Stone.AddCollisionTo(s.Collision); s.Land.AddCollisionTo(s.Collision);
        s.SeaStairGeometry.AddCollisionTo(s.seaStairCollision);
        s.Collision.Additional = s.seaStairCollision;
        s.BuildResidentAreas();
        s.People = Residents.Create(s, seed);
        if (includeSpecial) s.BuildSpecialContent();
        return s;
    }
    private void Box(Vector3 center, Vector3 size, Color? color = null) => Stone.Box(center, size, color ?? stone);
    private void Deck(float x0, float x1, float z0, float z1, float y, float thickness = .7f, Geometry? target = null) =>
        (target ?? Stone).Box(new((x0 + x1) / 2, y - thickness / 2, (z0 + z1) / 2), new(x1 - x0, thickness, z1 - z0), floor);
    private void Route(string name, params Vector3[] points) => Routes.Add(new(name, points.Select(Transform).ToArray()));
    private void Stair(Vector3 from, Vector3 to, float width, Geometry? junction = null, Geometry? target = null)
    {
        int count = (int)MathF.Ceiling((to.Y - from.Y) / .301f);
        Vector3 delta = (to - from) / count;
        for (int i = 0; i < count; i++)
        {
            var p = from + delta * (i + .5f); float y = from.Y + delta.Y * (i + 1);
            bool alongZ = Math.Abs(delta.Z) > Math.Abs(delta.X);
            Vector3 center = new(p.X, y - .5f, p.Z);
            Vector3 size = alongZ ? new(width, 1, Math.Abs(delta.Z)) : new(Math.Abs(delta.X), 1, width);
            (target ?? Stone).Box(center, size, pale);
            if (i == count - 1) junction?.Box(center, size, pale);
        }
    }
    private void Lamp(Vector3 p)
        => lampRequests.Add(p);
    private void PlaceLamps()
    {
        VaryLampRequests();
        // Freeze structural support before adding props: a lamp must never count
        // its own pedestal (or another lamp) as the floor holding it up.
        var support = new WalkableScene();
        Stone.AddCollisionTo(support); Land.AddCollisionTo(support);
        SeaStairGeometry.AddCollisionTo(support);
        foreach (var p in lampRequests)
        {
            if (!PlacementSupport.HasFlatSupport(support, p, new(.4f, .4f)))
            { RejectedLampCount++; continue; }
            BuildLamp(p);
        }
        lampRequests.Clear();
    }
    private void BuildLamp(Vector3 p)
    {
        Box(p + new Vector3(0, .15f, 0), new(.8f, .3f, .8f));
        Box(p + new Vector3(0, 1, 0), new(.2f, 1.5f, .2f));
        Box(p + new Vector3(0, 2.2f, 0), new(.8f, .2f, .8f), pale);
        var center = Transform(p + new Vector3(0, 1.9f, 0));
        Lights.Add(new(center, 17, center - new Vector3(17), center + new Vector3(17), X, Z, Lights.Count));
        LampBases.Add(Transform(p));
    }
    private void BuildIsland()
    {
        const int sides = 32;
        var rim = new Vector3[sides];
        float rx = random.Next(59, 65), rz = random.Next(59, 65);
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides, r = .97f + random.NextSingle() * .06f;
            rim[i] = new(MathF.Cos(a) * rx * r, -10 - random.NextSingle() * 2, MathF.Sin(a) * rz * r);
        }
        for (int i = 0; i < sides; i++)
        {
            Vector3 a = rim[i], b = rim[(i + 1) % sides];
            Land.Triangle(new(0, -6, 0), b, a, rock);
            Vector3 Foot(Vector3 p) => new(p.X * 1.13f, World.SeaFloor - .5f, p.Z * 1.13f);
            Land.Quad(a, b, Foot(b), Foot(a), rock);
        }
        Deck(-38, 38, -38, 38, 0, 14);
        // Buttresses frame the island without closing the four bridge gateways.
        foreach (float x in new[] { -35f, 35f })
            foreach (float z in new[] { -35f, 35f })
            {
                Box(new(x, -16, z), new(5, 36, 5));
                // Two one-unit jumps reach the buttress top with the normal jump.
                // Touch its inward edge and keep both bridge gateways clear.
                Box(new(x, .5f, z - MathF.Sign(z) * 3.5f), new(2, 1, 2));
            }
    }
    private void BuildNetwork()
    {
        // All four ports are owned halfway to the next island. No shared floor overlap.
        Deck(38, 96, -4, 4, 0);
        Deck(-96, -38, -4, 4, 0);
        Deck(-4, 4, -96, -38, 0); Deck(-4, 4, 38, 96, 0);
        foreach (float sign in new[] { -1f, 1f })
        {
            foreach (float side in new[] { -1f, 1f })
            {
                Box(new(sign * 67, .65f, side * 3.8f), new(58, 1.3f, .4f), pale);
                Box(new(side * 3.8f, .65f, sign * 67), new(.4f, 1.3f, 58), pale);
            }
            foreach (float d in new[] { 52f, 82f })
            {
                Box(new(sign * d, -32, 0), new(3, 62, 5));
                Box(new(0, -32, sign * d), new(5, 62, 3));
            }
            Route("east/west port", new(sign * 95.5f, 0, 0), new(sign * 38, 0, 0), Vector3.Zero);
            Route("north/south port", new(0, 0, sign * 95.5f), new(0, 0, sign * 38), Vector3.Zero);
        }
        foreach (float x in new[] { -10f, 10f }) Lamp(new(x, 0, 3));
        // Keep the entire .8-wide pedestal on the 8-wide bridge, inside its rails.
        Lamp(new(2.8f, 0, 66)); Lamp(new(-2.8f, 0, -66));
    }
    private void BuildHall()
    {
        int first = Stone.VertexCount;
        float h = HallHeight, end = -8 - h / .3f * .5f;
        // The center aisle remains open through both facades to the north bridge.
        foreach (float x in new[] { -30f, -20, -10, 10, 20, 30 })
        {
            Box(new(x, h / 2, -8), new(1.8f, h, 1.8f));
            Box(new(x, h / 2, -36), new(1.8f, h, 1.8f));
        }
        foreach (float z in new[] { -8f, -36 })
            foreach (float x in new[] { -25f, -15, 0, 15, 25 })
            {
                Stone.Arch(new(x, x == 0 ? 2 : 5, z), x == 0 ? 8.9f : 4.1f, .7f, 2, pale);
                Box(new(x, h - 1.2f, z), new(x == 0 ? 18.2f : 8.2f, 1, 1.8f));
            }
        foreach (float x in new[] { -30f, 30f })
        {
            Box(new(x, h / 2, -22), new(1.8f, h, 1.8f));
            foreach (float z in new[] { -29f, -15f })
            {
                Stone.Arch(new(x, 4, z), 6.1f, .7f, 2, pale, true);
            }
        }
        Deck(-30, 20, -36, -8, h); Deck(26, 30, -36, -8, h);
        Deck(20, 26, -36, end, h);
        Stair(new(23, 0, -8), new(23, h, end), 6);
        // Roof edges leave the staircase mouth and center aisle unobstructed.
        foreach (float x in new[] { -29.6f, 29.6f }) Box(new(x, h + .5f, -22), new(.5f, 1, 27), pale);
        Box(new(-5, h + .5f, -8.3f), new(49, 1, .5f), pale);
        Box(new(0, h + .5f, -35.7f), new(59, 1, .5f), pale);
        // Columns reach roof level and partly intersect the slabs along the rim.
        // Let the later roof slabs own the shared surface, retaining exposed caps
        // outside their footprint and the opening at the top of the stairs.
        Stone.RemoveCoplanarOverlaps(first);
        Lamp(new(-23, 0, -20)); Lamp(new(-24, h, -28));
        Route("hall approach", Vector3.Zero, new(16, 0, 0), new(23, 0, 0), new(23, 0, -7.3f));
        Route("hall stair and roof", new(23, 0, -7.3f), new(23, h, end - .7f), new(18, h, end - .7f), new(-20, h, -20));
    }
    private void BuildTower()
    {
        float roof = TowerFloors * 12;
        void Wall(int side, float fixedAxis, float lo, float hi, float door, float width)
        {
            bool alongZ = side >= 2;
            void Segment(float a, float b, float bottom = 0)
            {
                if (b <= a) return;
                Vector3 center = alongZ ? new(fixedAxis, (roof + bottom) / 2, (a + b) / 2)
                    : new((a + b) / 2, (roof + bottom) / 2, fixedAxis);
                Box(center, alongZ ? new(1, roof - bottom, b - a) : new(b - a, roof - bottom, 1));
            }
            if ((TowerEntranceMask & (1 << side)) == 0) { Segment(lo, hi); return; }
            Segment(lo, door - width / 2);
            Segment(door + width / 2, hi);
            Segment(door - width / 2, door + width / 2, 4);
        }
        Wall(0, 33.5f, 13, 31, 22, 4);
        Wall(1, 10.5f, 13, 31, 22, 4);
        // Side doors open onto the clear aisle before the first stair, rather
        // than into its low underside. Side walls retain ownership of corners.
        Wall(2, 12.5f, 10, 34, 12.5f, 3);
        Wall(3, 31.5f, 10, 34, 12.5f, 3);
        for (int side = 0; side < 4; side++)
        {
            if ((TowerEntranceMask & (1 << side)) == 0) continue;
            Vector3[] approach = side switch
            {
                0 => [new(0, 0, 20), new(8, 0, 36), new(22, 0, 36), new(22, 0, 18), new(22, 0, 12.5f)],
                1 => [Vector3.Zero, new(22, 0, 0), new(22, 0, 6), new(22, 0, 12.5f)],
                2 => [Vector3.Zero, new(8, 0, 6), new(8, 0, 12.5f), new(22, 0, 12.5f)],
                _ => [Vector3.Zero, new(35, 0, 6), new(35, 0, 12.5f), new(22, 0, 12.5f)]
            };
            Route($"tower entrance {side}", approach);
        }
        var route = new List<Vector3> { new(22, 0, 12.5f), new(18, 0, 13) };
        for (int level = 0; level < TowerFloors; level++)
        {
            float y = level * 12;
            Stair(new(18, y, 14), new(18, y + 6, 30), 4);
            Deck(14, 30, 30, 33, y + 6);
            Stair(new(26, y + 6, 30), new(26, y + 12, 14), 4);
            if (level + 1 < TowerFloors) Deck(14, 30, 11, 14, y + 12);
            route.AddRange([new(18, y + 6, 31), new(26, y + 6, 31),
                new(26, y + 12, 13), new(18, y + 12, 13)]);
        }
        Deck(13, 24, 11, 33, roof); Deck(28, 31, 11, 33, roof);
        Deck(24, 28, 11, 14, roof); Deck(24, 28, 30, 33, roof);
        foreach (float x in new[] { 12.5f, 31.5f })
        {
            if (ChaosTier > 0 && x == 12.5f)
            {
                // The new rooftop bridge enters through a real opening in the parapet.
                Box(new(x, roof + .6f, 10.5f), new(1, 1.2f, 1));
                Box(new(x, roof + .6f, 24.5f), new(1, 1.2f, 19));
            }
            else Box(new(x, roof + .6f, 22), new(1, 1.2f, 24));
        }
        foreach (float z in new[] { 10.5f, 33.5f }) Box(new(22, roof + .6f, z), new(18, 1.2f, 1));
        Lamp(new(14, 0, 23)); Lamp(new(14, roof, 24));
        Route("tower switchbacks", route.ToArray());
    }
    private void BuildSeaStair()
    {
        Deck(-28, -22, 86, 92, -20, target: SeaStairGeometry);
        Stair(new(-25, -20, 86), new(-25, 0, 38), 6, BridgeJunctionSurface, SeaStairGeometry);
        Route("sea arrival", new(-25, -20, 89), new(-25, -20, 86.7f), new(-25, 0, 37.3f), new(-25, 0, 35));
        Lamp(new(-29, 0, 34));
    }
    public void Upload()
    {
        Add(Stone, SurfaceSet.Architecture); Add(Land, SurfaceSet.Landscape);
        if (HasSeaStair)
        {
            Add(SeaStairGeometry, SurfaceSet.Architecture);
            seaStairChunk = Chunks[^1];
        }
        foreach (var prop in SpecialObjects) prop.Upload();
        ReleaseGeometry();
    }
    internal void SetOrigin(long originX, long originZ)
    {
        Vector3 next = WorldPosition.Offset(X, Z, originX, originZ), delta = next - Offset;
        if (next == Offset) return;
        Offset = next; Collision.Offset = next;
        foreach (var chunk in Chunks) chunk.Offset = next;
        foreach (var light in Lights) light.Offset = next;
        for (int i = 0; i < LampBases.Count; i++) LampBases[i] += delta;
        foreach (var route in Routes)
            for (int i = 0; i < route.Points.Length; i++) route.Points[i] += delta;
        foreach (var port in SkyPorts) port.Offset = next;
        foreach (var person in People) person.Shift(delta);
        foreach (var prop in SpecialObjects) prop.SetOrigin(next);
        SculptureFoot += delta; SculptureMin += delta; SculptureMax += delta;
        CompassCenter += delta; NumberPlateCenter += delta;
    }
    private void Add(Geometry geometry, SurfaceSet surface)
    {
        var p = geometry.Vertices;
        Vector3 min = p.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min);
        Vector3 max = p.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max);
        Chunks.Add(new() { Detailed = geometry.UploadTiled(), LocalMin = min, LocalMax = max, Offset = Offset,
            MaterialOffset = new((X % 32) * World.CellSize, 0, (Z % 32) * World.CellSize),
            Surface = surface, WallFinish = surface == SurfaceSet.Architecture ? WallFinish : Vector3.Zero,
            WallStrength = IdentityStrength,
            PrimeNumber = NumberIsPrime == true,
            DestinationPrimes = DestinationPrimes,
            DetailedTriangles = geometry.VertexCount / 3 });
    }
    public void ReleaseGeometry() { Stone.Clear(); Land.Clear(); SeaStairGeometry.Clear(); foreach (var prop in SpecialObjects) { prop.Geometry.Clear(); prop.PedestalGeometry.Clear(); } }
    public void Dispose() { DisposeWayfinding(); foreach (var c in Chunks) c.Dispose(); foreach (var prop in SpecialObjects) prop.Dispose(); Chunks.Clear(); ReleaseGeometry(); BridgeJunctionSurface.Clear(); }
}
