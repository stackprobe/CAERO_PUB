using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed record Viewpoint(string Name, Vector3 Position, Vector3 Target);
internal sealed record NightLight(Vector3 LocalPosition, float Radius, Vector3 LocalMin, Vector3 LocalMax, long IslandX = 0, long IslandZ = 0, int Index = 0)
{
    internal Vector3 Offset { get; set; }
    public Vector3 Position => LocalPosition + Offset;
    public Vector3 Min => LocalMin + Offset;
    public Vector3 Max => LocalMax + Offset;
    internal (long, long, int) Key => (IslandX, IslandZ, Index);
}

// Only island addresses are global. Every simulation/render position is relative to Origin.
internal sealed class World : IDisposable
{
    public const float WaterLevel = -18.75f, SeaFloor = -64;
    public const int DefaultSeed = 1701, CellSize = 192, LoadRadius = 2, KeepRadius = 3;
    private readonly Dictionary<(long X, long Z), Sector> sectors = [];
    private readonly bool upload;
    private Task<Sector>? pending;
    private (long X, long Z) pendingKey;
    private bool disposed;
    private readonly Queue<Sector> numberSigns = new();
    internal const double PrimeBudgetMilliseconds = .35;
    internal int LastPrimeWorkSteps { get; private set; }
    public long OriginX { get; private set; }
    public long OriginZ { get; private set; }
    public int OriginShiftCount { get; private set; }
    public int InstalledSectorCount { get; private set; }
    public int MeshUploadCount { get; private set; }
    public Vector3 EntryPosition { get; }
    public event Action<Vector3>? OriginShifted;
    public event Action<Vector3>? ViewTeleported;
    public int Seed { get; }
    public SeaPredator Predator { get; } = new();
    public int LoadedCount => sectors.Count;
    public bool Generating => pending != null;
    public IEnumerable<SceneChunk> Chunks => sectors.Values.SelectMany(s => s.Chunks);
    public IEnumerable<NightLight> Lights => sectors.Values.SelectMany(s => s.Lights);
    public IEnumerable<Resident> People => sectors.Values.SelectMany(s => s.People);
    public IEnumerable<Sector> Sectors => sectors.Values;
    // Start view is island-local at the immutable global address (0,0).
    public Viewpoint[] Views { get; }
    public static (long X, long Z) Cell(Vector3 position) =>
        (checked((long)Math.Floor(((double)position.X + CellSize / 2) / CellSize)),
         checked((long)Math.Floor(((double)position.Z + CellSize / 2) / CellSize)));
    public (long X, long Z) GlobalCell(Vector3 position)
    {
        var cell = Cell(position);
        return (checked(OriginX + cell.X), checked(OriginZ + cell.Z));
    }
    public WorldPosition AddressOf(Vector3 position) => new WorldPosition(OriginX, OriginZ, position).Normalized();
    public Vector3 LocalOf(WorldPosition address) => address.RelativeTo(OriginX, OriginZ);

    public World(bool upload = true, int seed = DefaultSeed, Vector3? start = null, long originX = 0, long originZ = 0)
    {
        this.upload = upload; Seed = seed;
        Views = [Sector.SpawnView(seed)];
        var entry = new WorldPosition(originX, originZ, start ?? Views[0].Position).Normalized();
        OriginX = entry.IslandX; OriginZ = entry.IslandZ; EntryPosition = entry.Local;
        EnsureAround(EntryPosition, LoadRadius);
        Predator.Reset(this, EntryPosition);
    }

    internal void Recenter(Explorer player)
    {
        var cell = Cell(player.Position);
        if (cell == (0, 0)) return;
        long x = checked(OriginX + cell.X), z = checked(OriginZ + cell.Z);
        Vector3 delta = new((float)cell.X * CellSize, 0, (float)cell.Z * CellSize);
        OriginX = x; OriginZ = z;
        Evict((x, z));
        foreach (var sector in sectors.Values) sector.SetOrigin(x, z);
        player.Position -= delta;
        Predator.Shift(-delta);
        OriginShiftCount++;
        OriginShifted?.Invoke(-delta);
    }

    internal void Teleport(Explorer player, WorldPosition address, float safetySeconds = 3, int radius = LoadRadius)
    {
        address = address.Normalized();
        OriginX = address.IslandX; OriginZ = address.IslandZ;
        Evict((OriginX, OriginZ));
        foreach (var sector in sectors.Values) sector.SetOrigin(OriginX, OriginZ);
        player.Position = address.Local;
        EnsureAround(address.Local, radius);
        Predator.Reset(this, player.Position, safetySeconds);
        ViewTeleported?.Invoke(player.Position);
    }

    internal void ReturnToStart(Explorer player, float safetySeconds = 3)
    {
        player.GoTo(Views[0]);
        Teleport(player, new(0, 0, Views[0].Position), safetySeconds);
    }

    // Explicit teleports synchronously prepare their destination; ordinary crossings do not.
    public void EnsureAround(Vector3 position, int radius = 1)
    {
        var center = GlobalCell(position);
        Evict(center);
        foreach (var key in Neighborhood(center, radius))
            if (!sectors.ContainsKey(key)) Install(Sector.Generate(Seed, key.X, key.Z));
    }

    public void Update(Vector3 position)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        UpdateNumberSigns();
        var center = GlobalCell(position);
        Evict(center);
        if (pending is { IsCompleted: true })
        {
            var ready = pending.GetAwaiter().GetResult(); pending = null;
            if (Within(pendingKey, center, KeepRadius) && !sectors.ContainsKey(pendingKey)) Install(ready);
            else ready.Dispose();
        }
        if (pending != null) return;
        foreach (var key in Neighborhood(center, LoadRadius).OrderBy(k =>
            Vector3.DistanceSquared(WorldPosition.Offset(k.X, k.Z, OriginX, OriginZ), position)))
        {
            if (sectors.ContainsKey(key)) continue;
            pendingKey = key;
            pending = Task.Run(() => Sector.Generate(Seed, key.X, key.Z));
            break;
        }
    }

    private void Install(Sector sector)
    {
        sector.SetOrigin(OriginX, OriginZ);
        if (upload) { sector.Upload(); MeshUploadCount += sector.Chunks.Count; }
        else sector.ReleaseGeometry();
        sectors.Add((sector.X, sector.Z), sector); InstalledSectorCount++;
        numberSigns.Enqueue(sector);
    }
    private void UpdateNumberSigns()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        LastPrimeWorkSteps = 0;
        // One shared budget for all loaded islands; round-robin avoids a hard
        // distant number blocking the small numbers on nearby islands.
        for (int steps = 0; steps < 64 && numberSigns.Count > 0; steps++)
        {
            var sector = numberSigns.Dequeue();
            if (!sector.WayfindingDisposed && !sector.NumberIsPrime.HasValue)
            {
                sector.StepNumberPrime();
                LastPrimeWorkSteps++;
                if (!sector.NumberIsPrime.HasValue) numberSigns.Enqueue(sector);
            }
            if (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds >= PrimeBudgetMilliseconds) break;
        }
    }
    private void Evict((long X, long Z) center)
    {
        bool removed = false;
        foreach (var key in sectors.Keys.Where(k => !Within(k, center, KeepRadius)).ToArray())
        { sectors[key].Dispose(); sectors.Remove(key); removed = true; }
        if (removed)
            for (int i = numberSigns.Count; i > 0; i--)
            {
                var sector = numberSigns.Dequeue();
                if (!sector.WayfindingDisposed && !sector.NumberIsPrime.HasValue) numberSigns.Enqueue(sector);
            }
    }
    private static bool Within((long X, long Z) a, (long X, long Z) b, int radius) =>
        Int128.Abs((Int128)a.X - b.X) <= radius && Int128.Abs((Int128)a.Z - b.Z) <= radius;
    private static IEnumerable<(long X, long Z)> Neighborhood((long X, long Z) c, int radius)
    {
        for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                Int128 x = (Int128)c.X + dx, z = (Int128)c.Z + dz;
                if (x >= long.MinValue && x <= long.MaxValue && z >= long.MinValue && z <= long.MaxValue) yield return ((long)x, (long)z);
            }
    }
    private IEnumerable<Sector> Nearby(float x, float z)
    {
        var lo = GlobalCell(new(x - .4f, 0, z - .4f));
        var hi = GlobalCell(new(x + .4f, 0, z + .4f));
        for (long cz = lo.Z; ; cz++)
        {
            for (long cx = lo.X; ; cx++)
            {
                if (sectors.TryGetValue((cx, cz), out var s)) yield return s;
                if (cx == hi.X) break;
            }
            if (cz == hi.Z) break;
        }
    }
    public float FloorAt(float x, float z, float feet) => SurfaceBelow(x, z, feet + .42f);
    public float SurfaceBelow(float x, float z, float maximum)
    {
        float floor = maximum >= SeaFloor ? SeaFloor : float.NegativeInfinity;
        foreach (var s in Nearby(x, z)) floor = Math.Max(floor, s.Collision.FloorAt(x, z, maximum));
        return floor;
    }
    public float SupportBelow(float x, float z, float maximum)
    {
        float floor = SurfaceBelow(x, z, maximum);
        floor = Math.Max(floor, SurfaceBelow(x + .35f, z, maximum));
        floor = Math.Max(floor, SurfaceBelow(x - .35f, z, maximum));
        floor = Math.Max(floor, SurfaceBelow(x, z + .35f, maximum));
        return Math.Max(floor, SurfaceBelow(x, z - .35f, maximum));
    }
    public float CeilingAbove(Vector3 eye)
    {
        float ceiling = float.PositiveInfinity;
        foreach (var s in Nearby(eye.X, eye.Z)) ceiling = Math.Min(ceiling, s.Collision.CeilingAt(eye.X, eye.Z, eye.Y));
        return ceiling;
    }
    public bool Blocked(Vector3 p) => Nearby(p.X, p.Z).Any(s => s.Collision.Blocked(p));
    internal bool SphereBlocked(Vector3 p, float radius, bool includeSeabed = true)
    {
        if (includeSeabed && p.Y - radius < SeaFloor) return true;
        var lo = Cell(p - new Vector3(radius)); var hi = Cell(p + new Vector3(radius));
        for (long z = lo.Z; z <= hi.Z; z++)
            for (long x = lo.X; x <= hi.X; x++)
                if (sectors.TryGetValue((checked(OriginX + x), checked(OriginZ + z)), out var s) && s.Collision.SphereBlocked(p, radius)) return true;
        return false;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { pending?.GetAwaiter().GetResult().Dispose(); }
        finally { foreach (var s in sectors.Values) s.Dispose(); sectors.Clear(); numberSigns.Clear(); }
    }
}
