using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace Caero;

internal static class FloatingOriginChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Floating origin check failed: " + message); }

    public static void Run()
    {
        AddressesAndGeometry();
        BoundaryCycles();
        MovementSeams();
        PendingAndTeleport();
        Saves();
        Console.WriteLine("FLOATING ORIGIN: 64-bit seeded addresses, local meshes/collision, every-boundary rebasing, actor/light continuity, streaming, huge-address saves and legacy position migration passed.");
    }

    private static void AddressesAndGeometry()
    {
        foreach (var (x, z, expected) in new[] {
            (1_000_000_000_000L, -1_000_000_000_000L, "178AB15CFB07AD9B6B86CD6676780649"),
            (long.MaxValue - 1024, long.MinValue + 1024, "36F342FDA0EB7AFFCFE87BE3B46BBB78"),
            (long.MinValue, long.MaxValue, "0C0035802C19AAFB264B0A0E7D23173A") })
        {
            var seed = Sector.CoordinateSeedAt(x, z);
            Check($"{seed.X:X8}{seed.Y:X8}{seed.Z:X8}{seed.W:X8}" == expected, "independent Python SHA-512 vectors for 64-bit addresses");
        }
        foreach (var (x, z) in new[] { (1_000_000_000_000L, -1_000_000_000_000L),
            (-1_000_000_000_000L, 1_000_000_000_000L), (long.MaxValue - 1024, long.MinValue + 1024) })
        {
            using var a = Sector.Generate(1701, x, z); using var b = Sector.Generate(1701, x, z);
            Check(a.Stone.Vertices.SequenceEqual(b.Stone.Vertices) && a.Land.Vertices.SequenceEqual(b.Land.Vertices), "huge addresses regenerate exactly");
            Check(a.Stone.Vertices.Concat(a.Land.Vertices).All(p => Math.Abs(p.X) <= 96.01f && Math.Abs(p.Z) <= 96.01f && float.IsFinite(p.Y)),
                "absolute addresses never enter vertex coordinates");
            Check(a.SculptureCount == 1 && a.WeedRoots.Count >= a.MinimumWeedCount && a.WingCount > 0, "far decoration survives address expansion");
            Vector3[] original = a.Stone.Vertices.ToArray();
            var collision = a.Collision;
            a.SetOrigin(x - 1, z + 1);
            Check(a.Offset == new Vector3(192, 0, -192) && ReferenceEquals(collision, a.Collision) && a.Stone.Vertices.SequenceEqual(original),
                "offset changes neither mesh vertices nor collision buckets");
            var point = a.Transform(new(0, 0, 20));
            Check(Math.Abs(a.Collision.FloorAt(point.X, point.Z, .01f)) < .01f, "offset collision queries match the rendered island");
            foreach (double step in new[] { 192 * .31, 192 * .17, 192 * .26, 192 * .09 })
                Check(Math.Abs(Math.IEEERemainder(Renderer.WavePhase(x + 1, step) - Renderer.WavePhase(x, step) - step, Math.Tau)) < 1e-9,
                    "water phases remain continuous at huge addresses");
        }
    }

    private static void BoundaryCycles()
    {
        using var world = new World(upload: false, start: new(95.9f, 50, 0), originX: 1_000_000_000_000, originZ: -1_000_000_000_000);
        var player = new Explorer { Position = world.EntryPosition, Flying = true, Yaw = .7f, Pitch = -.2f };
        world.EnsureAround(player.Position, World.KeepRadius);
        int initialCount = world.LoadedCount;
        var sector = world.Sectors.First(s => s.X == world.OriginX && s.Z == world.OriginZ);
        var collision = sector.Collision;
        var resident = world.People.First();
        WorldPosition person = world.AddressOf(resident.Position), anchor = world.AddressOf(resident.Anchor);
        world.Predator.Stage(world.Predator.Position, world.Predator.Forward, PredatorState.Hunting);
        WorldPosition fish = world.AddressOf(world.Predator.Position);
        var schedule = new LanternSchedule(12345);
        var lamps = world.Lights.ToDictionary(l => l.Key, l => (Address: world.AddressOf(l.Position), Brightness: schedule.Brightness(l, 302)));
        int installed = world.InstalledSectorCount, uploads = world.MeshUploadCount;
        var timings = new List<double>();
        Vector3 totalShift = Vector3.Zero; world.OriginShifted += delta => totalShift += delta;
        for (int i = 0; i < 512; i++)
        {
            long beforeX = world.OriginX;
            long tick = Stopwatch.GetTimestamp();
            player.Advance(.01f, world, i % 2 == 0 ? Vector3.UnitX : -Vector3.UnitX, false, false, false, false, true);
            timings.Add(Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
            Check(world.OriginX == beforeX + (i % 2 == 0 ? 1 : -1) && World.Cell(player.Position) == (0, 0), "every ordinary crossing rebases immediately in both directions");
            Check(world.AddressOf(resident.Position) == person && world.AddressOf(resident.Anchor) == anchor, "resident pose and patrol survive rebasing");
            Check(world.AddressOf(world.Predator.Position) == fish && world.Predator.State == PredatorState.Hunting && world.Predator.DiveSeconds == 25,
                "fish location and aggression survive rebasing");
            Check(ReferenceEquals(collision, sector.Collision) && world.InstalledSectorCount == installed && world.MeshUploadCount == uploads,
                "crossing does not regenerate islands, collision or GPU meshes");
            foreach (var light in world.Lights)
                Check(Vector3.Distance(world.LocalOf(lamps[light.Key].Address), light.Position) < .00015f
                    && lamps[light.Key].Brightness == schedule.Brightness(light, 302),
                    "lamp position, identity and fade schedule do not jump");
        }
        Check(world.OriginShiftCount == 512 && totalShift == Vector3.Zero && player.Flying && player.Yaw == .7f && player.Pitch == -.2f,
            "boundary oscillation keeps camera and origin consistent");
        timings.Sort();
        Console.WriteLine($"REBASE COST: 512 actual crossings, no regeneration/uploads; median {timings[timings.Count / 2]:F3} ms, max {timings[^1]:F3} ms, {initialCount} initially loaded / {world.LoadedCount} retained islands (CPU test).");
    }

    private static void MovementSeams()
    {
        using var world = new World(upload: false);
        foreach (bool swimming in new[] { false, true })
            foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
            {
                float height = swimming ? World.WaterLevel - 5 : Explorer.EyeHeight;
                Vector3 start = direction * 95.9f + new Vector3(0, height, 0);
                if (swimming) start += Math.Abs(direction.X) > 0 ? new Vector3(0, 0, 90) : new Vector3(90, 0, 0);
                var player = new Explorer();
                world.Teleport(player, new(0, 0, start));
                player.Advance(.01f, world, direction, false, false, false, false, true);
                var address = world.AddressOf(player.Position);
                Check(address.IslandX == (long)direction.X && address.IslandZ == (long)direction.Z && World.Cell(player.Position) == (0, 0),
                    "walking/swimming cross both signed axes");
                Check(!world.Blocked(player.Position) && (swimming ? player.Swimming : player.Grounded) && player.SplashImpact == 0,
                    "seams preserve support, swimming and motion events");
            }
        var jumper = new Explorer();
        world.Teleport(jumper, new(0, 0, new(95.9f, Explorer.EyeHeight, 0)));
        jumper.Advance(.01f, world, Vector3.UnitX, true, true, false, false, true);
        Check(world.OriginX == 1 && jumper.Jumped && jumper.Position.Y > Explorer.EyeHeight, "jump crosses the seam without losing its impulse or event");
        float peak = jumper.Position.Y;
        for (int i = 0; i < 200; i++)
        { jumper.Advance(.02f, world, Vector3.Zero, false, false, false, false); peak = Math.Max(peak, jumper.Position.Y); }
        Check(peak > Explorer.EyeHeight + 1.4f && jumper.Grounded && Math.Abs(jumper.Position.Y - Explorer.EyeHeight) < .01f,
            "jump momentum, apex and landing survive origin shifting");
    }

    private static void PendingAndTeleport()
    {
        using var world = new World(upload: false, start: new(95.9f, 50, 0), originX: long.MaxValue - 1024, originZ: long.MinValue + 1024);
        var player = new Explorer { Position = world.EntryPosition, Flying = true };
        world.Update(new(192, 50, 0));
        Check(world.Generating, "worker is in flight before a boundary crossing");
        player.Advance(.01f, world, Vector3.UnitX, false, false, false, false, true);
        Wait(world, player.Position);
        foreach (var s in world.Sectors)
            Check(s.Offset == WorldPosition.Offset(s.X, s.Z, world.OriginX, world.OriginZ) && s.Offset.Length() < 900,
                "worker completion uses the current origin, not its dispatch origin");
        world.Update(new(384, 50, 0));
        world.Teleport(player, new(long.MinValue + 1024, long.MaxValue - 1024, new(0, 50, 0)), radius: 0);
        Wait(world, player.Position);
        Check(world.LoadedCount <= 49 && world.Sectors.All(s => Int128.Abs((Int128)s.X - world.OriginX) <= 3 && Int128.Abs((Int128)s.Z - world.OriginZ) <= 3),
            "opposite 64-bit destinations discard stale worker results without subtraction overflow");
        world.ReturnToStart(player);
        Check(world.OriginX == 0 && world.OriginZ == 0 && player.Position == world.Views[0].Position, "return to true start does not materialize a huge floating coordinate");
    }

    private static void Wait(World world, Vector3 position)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            world.Update(position);
            if (!world.Generating && world.LoadedCount >= 25) return;
            Thread.Sleep(1);
        } while (timer.Elapsed.TotalSeconds < 30);
        throw new InvalidOperationException("Floating-origin streaming timed out.");
    }

    private static void Saves()
    {
        string path = Path.Combine(SaveStore.DirectoryPath, "generated-view.json");
        byte[]? backup = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            using var world = new World(upload: false, start: new(77.25f, 50, -25.5f), originX: long.MaxValue - 1024, originZ: long.MinValue + 1024);
            var player = new Explorer { Position = world.EntryPosition, Yaw = .7f, Pitch = -.2f, Flying = true };
            Check(SaveStore.Save(player, world), "huge-address save succeeds");
            var view = SaveStore.Load();
            Check(view is { CoordinateVersion: 1, Flying: true } && view.IslandX == world.OriginX && view.IslandZ == world.OriginZ
                && new Vector3(view.X, view.Y, view.Z) == player.Position && view.Yaw == player.Yaw && view.Pitch == player.Pitch,
                "JSON round trip preserves full 64-bit addresses and fractional local position");
            File.WriteAllText(path, JsonSerializer.Serialize(new SavedView(-114.38038f, -18.725819f, 109.946686f, .7f, -.2f, true, 1701, 3)));
            var migrated = SaveStore.Load();
            var expected = new WorldPosition(0, 0, new(-114.38038f, -18.725819f, 109.946686f)).Normalized();
            Check(migrated is { CoordinateVersion: 1, IslandX: -1, IslandZ: 1, Flying: true, Seed: 1701 }
                && new Vector3(migrated.X, migrated.Y, migrated.Z) == expected.Local, "legacy v3 absolute save keeps the same island, view and world seed");
            File.WriteAllText(path, JsonSerializer.Serialize(new SavedView(0, 2, 0, 0, 0, false, 1701, 3, 0, 0, 99)));
            Check(SaveStore.Load() == null, "unsupported coordinate format rejected");
        }
        finally
        {
            if (backup != null) File.WriteAllBytes(path, backup);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}
