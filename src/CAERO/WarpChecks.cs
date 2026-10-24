using System.Numerics;

namespace Caero;

internal static class WarpChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Warp check failed: " + message); }

    internal static void Run()
    {
        foreach (var (dx, dz, destination) in new[] {
            (0, -1, "北へ 15 島"), (1, -1, "北へ 15 島 東へ 15 島"),
            (1, 0, "東へ 15 島"), (1, 1, "南へ 15 島 東へ 15 島"),
            (0, 1, "南へ 15 島"), (-1, 1, "南へ 15 島 西へ 15 島"),
            (-1, 0, "西へ 15 島"), (-1, -1, "北へ 15 島 西へ 15 島") })
            Check(new WarpTerminal(dx, dz, 15, default, default).ConfirmationMessage
                == $"ワープしますか？　ワープ先は {destination} です。", "eight-direction confirmation text");
        Check(new WarpTerminal(0, -1, 55, default, default).ConfirmationMessage
            == "ワープしますか？　ワープ先は 北へ 55 島 です。", "north confirmation text");
        var rotations = new HashSet<int>(); bool roof = false, inside = false;
        var stairSides = new HashSet<int>();
        foreach (int seed in new[] { 1701, 48291, -31, 1, 2, 3, 4, 5 })
            foreach (var (x, z) in new[] { (-43, -33), (6, 0), (15, 10) })
            {
                using var sector = Sector.Generate(seed, x, z);
                using var repeat = Sector.Generate(seed, x, z);
                rotations.Add(sector.Rotation);
                Check(sector.FloatingCourtStairTurns is >= 1 and <= 3
                    && sector.FloatingCourtStairTurns == repeat.FloatingCourtStairTurns,
                    "stairs reproducibly choose a face other than the tower passage");
                if (stairSides.Add(sector.FloatingCourtStairTurns))
                {
                    using var stairWorld = new World(upload: false, seed: seed, originX: x, originZ: z);
                    var stairs = stairWorld.Sectors.Single(s => s.X == x && s.Z == z);
                    WalkCourt(stairWorld, stairs);
                }
                Check(sector.WarpFloor == repeat.WarpFloor && sector.WarpTerminals.SequenceEqual(repeat.WarpTerminals),
                    "independent island-seeded floor, machine positions and distances survive regeneration");
                Check(sector.WarpFloor >= 0 && sector.WarpFloor <= sector.FloatingCourtFloors, "floor includes ground and roof");
                roof |= sector.WarpFloor == sector.FloatingCourtFloors;
                inside |= sector.WarpFloor < sector.FloatingCourtFloors;
                Check(sector.WarpTerminals.Count == 8 && sector.WarpTerminals.Select(t => (t.Dx, t.Dz)).Distinct().Count() == 8,
                    "eight distinct world compass directions");
                foreach (var terminal in sector.WarpTerminals)
                {
                    bool cardinal = terminal.Dx == 0 || terminal.Dz == 0;
                    Check(terminal.Distance >= (cardinal ? 30 : 20) && terminal.Distance <= (cardinal ? 70 : 50), "inclusive direction-specific distance range");
                    Check(terminal.Destination(x, z, out long dx, out long dz)
                        && dx == x + terminal.Dx * terminal.Distance && dz == z + terminal.Dz * terminal.Distance,
                        "diagonal travel advances by the full distance on both island axes");
                    Vector3 outward = sector.Transform(terminal.LocalPosition) - sector.WarpArrivalPosition;
                    Check(Vector3.Dot(Vector3.Normalize(outward), Vector3.Normalize(new Vector3(terminal.Dx, 0, terminal.Dz))) > .999f,
                        "machine positions stay aligned to the world compass through building rotations");
                }
                var panel = sector.WarpNumberPanel!;
                Check(panel.Number == sector.IslandNumber && panel.Width <= 6 && panel.Height <= 6, "room floor has its own bounded island number");
                Check(Vector3.Distance(sector.Transform(panel.Forward) - sector.Offset, -Vector3.UnitZ) < .001f, "number panel has consistent world orientation");
                foreach (var point in panel.Surface.Vertices)
                {
                    Vector3 p = sector.Transform(point);
                    Check(Math.Abs(sector.Collision.FloorAt(p.X, p.Z, p.Y + .01f) - p.Y) < .002f, "underlined panel is flush with a supported floor");
                }
                var center = sector.WarpArrivalPosition;
                Check(!sector.Collision.SurfaceOverlapsBox(center + new Vector3(-.4f, .03f, -.4f), center + new Vector3(.4f, 3, .4f)),
                    "arrival area has unobstructed space for the drop");
            }
        Check(rotations.Count == 4 && roof && inside, "all rotations, interior warp floors and roof warp floors covered");
        Check(stairSides.Count == 3, "all three non-passage stair faces walked in both directions");
        foreach (var cell in new[] { (0, 0), (1, -1), (3, 0), (5, 0) })
        {
            using var near = Sector.Generate(1701, cell.Item1, cell.Item2);
            Check(!near.WarpFloor.HasValue && near.WarpTerminals.Count == 0, "starting neighborhood has rooftop fallback instead of a warp room");
            Vector3 p = near.WarpArrivalPosition;
            Check(Math.Abs(near.Collision.FloorAt(p.X, p.Z, p.Y + .01f) - p.Y) < .002f && !near.Collision.Blocked(p + Vector3.UnitY * Explorer.EyeHeight),
                "fallback is a clear roof on the enclosed stair tower");
        }
        var edge = new WarpTerminal(1, -1, 50, default, default);
        Check(!edge.Destination(long.MaxValue - 49, 0, out _, out _)
            && !edge.Destination(0, long.MinValue + 49, out _, out _), "world boundaries never wrap island addresses");
        using var world = new World(upload: false, originX: 0, originZ: 30);
        var source = world.Sectors.First(s => s.X == 0 && s.Z == 30);
        foreach (var terminal in source.WarpTerminals)
        {
            var target = new WarpTarget(source, terminal);
            Vector3 facing = source.Transform(terminal.LocalFacing) - source.Offset;
            Vector3 eye = target.Point + facing * 2 + Vector3.UnitY * .6f;
            Check(WarpTravel.Target(world, eye, Vector3.Normalize(target.Point - eye))?.Terminal == terminal,
                "each machine can be examined from its clear front approach");
            Check(WarpTravel.Target(world, eye, Vector3.Normalize(eye - target.Point)) == null, "machine behind the player is not selected");
        }
        // Exercise every external flight in both directions with real step physics.
        WalkCourt(world, source);
        source = world.Sectors.First(s => s.X == 0 && s.Z == 30);
        var chosen = source.WarpTerminals[0];
        chosen.Destination(source.X, source.Z, out long expectedX, out long expectedZ);
        var player = new Explorer { Flying = true };
        Vector3 notified = new(float.NaN);
        world.ViewTeleported += p => notified = p;
        var conversation = new Conversation();
        var machine = new WarpTarget(source, chosen);
        int departures = 0;
        void Examine() => Check(WarpTravel.Examine(world, player, machine, conversation, () => departures++), "machine opens confirmation");
        Check(!WarpTravel.Examine(world, player, machine, conversation), "flight prevents opening the machine confirmation");
        player.Flying = false;
        Examine();
        Check(conversation.AwaitingChoice && conversation.Text == chosen.ConfirmationMessage
            && !world.WarpInProgress && departures == 0, "E displays message and choices without starting travel");
        Check(!world.ConsumeWarpStart(), "opening the confirmation emits no warp sound");
        conversation.Update(Conversation.ChoiceInputDelay);
        ConversationInput.Handle(conversation, key => key is Raylib_cs.KeyboardKey.Right or Raylib_cs.KeyboardKey.E);
        Check(!conversation.Active && !world.WarpInProgress && departures == 0, "No closes without travel");
        Check(!world.ConsumeWarpStart(), "declining emits no warp sound");
        Examine();
        ConversationInput.Handle(conversation, key => key == Raylib_cs.KeyboardKey.Escape);
        conversation.Advance();
        Check(!conversation.Active && !world.WarpInProgress && departures == 0, "Escape discards pending travel");
        Examine();
        conversation.Update(Conversation.ChoiceInputDelay);
        ConversationInput.Handle(conversation, key => key == Raylib_cs.KeyboardKey.E);
        conversation.Advance();
        Check(!conversation.Active && world.WarpInProgress && departures == 1, "Yes starts travel exactly once");
        Check(world.ConsumeWarpStart() && !world.ConsumeWarpStart(), "confirmed warp emits its sound once");
        Check(world.OriginX == source.X && world.OriginZ == source.Z, "confirmation starts the transition without synchronous relocation");
        WaitForArrival(world, player, expectedX, expectedZ);
        Check(world.OriginX == expectedX && world.OriginZ == expectedZ && !player.Flying && notified == player.Position,
            "destination is generated, origin and listeners move to actual arrival, and flight resets");
        CheckArrival(world, player, true);
        world.Teleport(player, new(0, 30, Vector3.Zero), radius: 0);
        source = world.Sectors.Single(s => s.X == 0 && s.Z == 30);
        Check(WarpTravel.Travel(world, player, new(source, new(0, -1, 30, default, default))), "travel to a roomless island succeeds");
        WaitForArrival(world, player, 0, 0);
        CheckArrival(world, player, false);
        TransitionChecks(world, player);
        Console.WriteLine("WARP: seeded 8-way terminals, all rotations, random floors/roofs, safe addresses, E targets, all 3 non-passage stair faces walked both ways, generated destinations, roof fallback and arrival drops passed.");
    }

    private static void WalkCourt(World world, Sector sector)
    {
        // Snapshot addresses before walking: streaming may rebase the world.
        var routes = sector.Routes.Where(r => r.Name.StartsWith("floating court") || r.Name == "warp room approach")
            .Select(r => (r.Name, Points: r.Points.Select(world.AddressOf).ToArray())).ToArray();
        foreach (var route in routes)
        {
            ProceduralChecks.Walk(world, route.Name, route.Points);
            ProceduralChecks.Walk(world, route.Name + " reverse", route.Points.Reverse().ToArray());
        }
    }

    private static void CheckArrival(World world, Explorer player, bool room)
    {
        var sector = world.Sectors.Single(s => s.X == world.OriginX && s.Z == world.OriginZ);
        Check(sector.WarpFloor.HasValue == room, "destination selects warp room or enclosed roof as appropriate");
        Vector3 floor = sector.WarpArrivalPosition;
        Check(Vector3.Distance(player.Position, floor + Vector3.UnitY * (Explorer.EyeHeight + WarpTravel.ArrivalDrop)) < .002f,
            "spawn begins slightly above the destination floor");
        Pump(world, player, () => !world.WarpInProgress, fall: true);
        Check(Math.Abs(player.Position.Y - floor.Y - Explorer.EyeHeight) < .002f && player.Grounded && !world.Blocked(player.Position),
            "arrival falls and lands safely instead of hovering or intersecting geometry");
    }

    private static void WaitForArrival(World world, Explorer player, long x, long z)
        => Pump(world, player, () => world.OriginX == x && world.OriginZ == z);

    private static void Pump(World world, Explorer player, Func<bool> done, bool fall = false)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Check(timeout.Elapsed.TotalSeconds < 30, "background warp generation finishes");
            int installed = world.InstalledSectorCount;
            world.AdvanceWarp(.02, player); world.Update(player.Position);
            Check(world.InstalledSectorCount - installed <= 1, "at most one island is installed per frame");
            if (fall && (world.WarpFalling || !world.WarpInProgress))
                player.Advance(.02f, world, Vector3.Zero, false, false, false, false);
            Thread.Sleep(1);
        }
    }

    private static void TransitionChecks(World world, Explorer player)
    {
        world.Teleport(player, new(0, 0, world.Views[0].Position), radius: 0);
        using var gate = new ManualResetEventSlim(false);
        int mainThread = Environment.CurrentManagedThreadId, generationThread = mainThread;
        int installed = world.InstalledSectorCount;
        try
        {
            Check(world.BeginWarp(30, 30, Vector3.UnitX, () => {
                generationThread = Environment.CurrentManagedThreadId;
                gate.Wait(); return Sector.Generate(world.Seed, 30, 30);
            }), "background transition begins");
            Check(world.ConsumeWarpStart(), "departure sound is ready before destination generation completes");
            Check(!world.BeginWarp(40, 40, Vector3.UnitX), "repeated activation cannot start another warp");
            Check(!world.ConsumeWarpStart(), "repeated activation emits no extra warp sound");
            for (int frame = 0; frame < 200; frame++)
            { world.AdvanceWarp(.02, player); world.Update(player.Position); }
            Check(world.WarpInProgress && world.WarpStrength > .99f && world.OriginX == 0 && world.OriginZ == 0
                && world.InstalledSectorCount == installed && !world.WarpFalling,
                "slow generation extends the animated departure without blocking updates, moving or dropping the player");
        }
        finally { gate.Set(); }
        WaitForArrival(world, player, 30, 30);
        Check(generationThread != mainThread, "island geometry is generated off the render thread");
        Check(world.WarpInProgress && world.WarpStrength > .99f && !world.WarpFalling,
            "arrival remains covered until nearby floors are ready");
        if (world.GenerationCell is { } next)
        {
            var nearest = (from z in Enumerable.Range(-2, 5) from x in Enumerable.Range(-2, 5)
                let cell = (X: 30L + x, Z: 30L + z)
                where !world.Sectors.Any(s => s.X == cell.X && s.Z == cell.Z)
                orderby Vector3.DistanceSquared(WorldPosition.Offset(cell.X, cell.Z, 30, 30), player.Position)
                select cell).First();
            Check(next == nearest, "neighbor generation prioritizes distance from the actual arrival position");
        }
        CheckArrival(world, player, true);
        Check(world.WarpStrength == 0, "effect returns exactly to normal");

        Check(world.BeginWarp(60, 60, Vector3.UnitZ, () => throw new InvalidOperationException("warp test failure")), "failure case begins");
        Pump(world, player, () => !world.WarpInProgress);
        Check(world.OriginX == 30 && world.OriginZ == 30 && world.TakeWarpFailure() == "warp test failure",
            "failed generation restores the source without stranding the player or leaving input locked");

        using var cancellation = new ManualResetEventSlim(false);
        var abandonedResult = new TaskCompletionSource<Sector>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            world.BeginWarp(60, 60, Vector3.UnitZ, () => {
                cancellation.Wait();
                var ready = Sector.Generate(world.Seed, 60, 60);
                abandonedResult.SetResult(ready); return ready;
            });
            world.Teleport(player, new(30, 30, player.Position), radius: 0);
            Check(!world.ConsumeWarpStart(), "cancellation clears an unplayed warp sound");
            Check(!world.WarpInProgress && world.OriginX == 30, "explicit teleport cancels an unfinished warp without waiting for it");
        }
        finally { cancellation.Set(); }
        var cleanup = System.Diagnostics.Stopwatch.StartNew();
        while (!abandonedResult.Task.IsCompletedSuccessfully || !abandonedResult.Task.Result.WayfindingDisposed)
        {
            Check(cleanup.Elapsed.TotalSeconds < 30, "cancelled generation releases its completed CPU island");
            world.Update(player.Position); Thread.Sleep(1);
        }
        Check(world.OriginX == 30 && world.OriginZ == 30, "cancelled work never installs a stale destination");
        Console.WriteLine("WARP TRANSITION: nonblocking activation/updates, delayed generation, safe arrival, proximity order, one install/frame, recovery, cancellation and failure passed.");
    }
}
