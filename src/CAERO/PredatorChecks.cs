using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class PredatorChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Predator check failed: " + message); }
    public static void Run()
    {
        PhaseTimeline();
        ProximityAttacks();
        WaterEntrySpawns();
        StartupPursuits();
        using var world = new World(upload: false);
        var fish = world.Predator;
        var player = new Explorer(); player.GoTo(world.Views[0]);
        Check(fish.Active && fish.Position.Y < World.WaterLevel - 18, "initial deep-water spawn");
        Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "whole body spawns clear of geometry and seabed");
        Vector3 start = fish.Position;
        for (int i = 0; i < 300; i++)
        {
            Check(!fish.Update(.1f, world, player), "player on land is safe");
            Check(fish.State == PredatorState.Patrol && fish.DiveSeconds == 0, "surface cannot aggro the fish");
            Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "slow patrol keeps full body clear");
        }
        Check(Vector3.Distance(fish.Position, start) > 2 && Vector3.Distance(fish.Position, start) < 34, "slow moving patrol, not stationary or fast");
        Check(world.SphereBlocked(new(82, -40, 0), 7), "bridge pier blocks the fish volume");
        Check(world.SphereBlocked(new(96, World.SeaFloor + 3, 96), 4), "analytic seabed blocks the fish volume");
        var wall = new Geometry(); wall.Box(new(0, 0, 0), new(.08f, 20, 20), Color.Gray);
        var scene = new WalkableScene(); wall.AddCollisionTo(scene);
        Check(scene.SphereBlocked(new(6.9f, 0, 0), 7) && !scene.SphereBlocked(new(7.2f, 0, 0), 7), "thin wall collision uses the full body radius");

        foreach (float dt in new[] { .01f, .05f, .1f })
        {
            fish.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Patrol);
            player.Position = new(96, -48.5f, 120);
            bool eaten = false, hunt = false;
            for (int i = 0; i < (int)(10 / dt); i++)
            {
                eaten = fish.Update(dt, world, player);
                hunt |= fish.State == PredatorState.Hunting;
                Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "pursuit cannot penetrate columns, islands or seabed");
                if (eaten) break;
            }
            Check(hunt && eaten, $"proximity bypasses the timer and leads to an open-mouth swallow: dt={dt}");
            Check(player.Position == world.Views[0].Position && !player.Flying && !player.Swimming && player.SplashImpact == 0,
                "swallow returns to true start and resets movement state");
            Check(fish.State == PredatorState.Patrol && fish.DiveSeconds == 0 && world.LoadedCount >= 25, "respawn prepares safe starting neighborhood");
            player.Position = fish.Mouth + Vector3.UnitY * .9f;
            Check(!fish.Update(2, world, player) && fish.State == PredatorState.Patrol, "post-swallow grace prevents immediate repeat attacks");
        }
        player.Position = new(96, World.WaterLevel + .1f, -96);
        Check(!fish.Update(.1f, world, player) && fish.State == PredatorState.Patrol && fish.DiveSeconds == 0 && !fish.TakingDetour, "surfacing ends pursuit and resets dive timer/route");
        foreach (bool flying in new[] { false, true })
        {
            fish.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Hunting);
            player.Position = new(96, -49, 74); player.Flying = flying;
            bool eaten = false, alerted = false;
            for (int i = 0; i < 600; i++)
            {
                eaten = fish.Update(.05f, world, player);
                alerted |= fish.State != PredatorState.Patrol;
                if (eaten) break;
            }
            Check(alerted && eaten, $"approaching from directly behind causes a turn and a swallow, including underwater flight: flying={flying}, state={fish.State}, position={fish.Position}");
        }
        player.Flying = false;
        fish.Stage(new(96, -56, 96), Vector3.UnitZ, PredatorState.Hunting);
        player.Position = new(96, -33, 105);
        bool caughtAbove = false;
        for (int i = 0; i < 600; i++)
        {
            if (fish.Update(.05f, world, player)) { caughtAbove = true; break; }
            Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "upward pursuit remains above the seabed");
        }
        Check(caughtAbove, $"can rise from near seabed and attack player overhead: position={fish.Position}, forward={fish.Forward}");
        fish.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Hunting);
        player.Position = new(96, World.SeaFloor + Explorer.EyeHeight, 115);
        bool caughtOnFloor = false;
        for (int i = 0; i < 600; i++)
        {
            if (fish.Update(.05f, world, player)) { caughtOnFloor = true; break; }
            Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "seabed-level pursuit keeps the fish clear");
        }
        Check(caughtOnFloor, $"can catch a player standing on seabed: position={fish.Position}, forward={fish.Forward}");
        for (int side = 0; side < 8; side++)
            foreach (float height in new[] { -49f, World.SeaFloor + Explorer.EyeHeight })
            {
                fish.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Hunting);
                float angle = side * MathF.Tau / 8;
                player.Position = new(96 + MathF.Sin(angle) * 18, height, 96 + MathF.Cos(angle) * 18);
                bool caught = false;
                for (int i = 0; i < 600; i++)
                {
                    if (fish.Update(.05f, world, player)) { caught = true; break; }
                    Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "all-direction attacks remain collision-free");
                }
                Check(caught, $"catch from all eight directions, including seabed: side={side}, height={height}, position={fish.Position}, forward={fish.Forward}");
            }
        fish.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Hunting);
        player.Flying = true; player.Position = new(96, World.WaterLevel + 1, 96);
        Check(!fish.Update(.1f, world, player) && fish.State == PredatorState.Patrol, "flight above water remains safe"); player.Flying = false;

        fish.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Hunting);
        player.Position = new(70, -40, 6);
        for (int i = 0; i < 400; i++)
        {
            if (fish.Update(.05f, world, player)) break;
            Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "coastal hunt remains collision-free while turning");
        }
        player.Position = new(3840, 0, -3840); world.EnsureAround(player.Position, 0);
        fish.Update(.1f, world, player);
        Check(fish.Active && Vector3.Distance(fish.Position, player.Position) < 200 && world.LoadedCount <= 49, "long travel relocates one bounded predator");
        Console.WriteLine("PREDATOR: 5/15/25-second phases, seabed cruising, real startup detours, full-body collision, three frame rates, swallow/start reset, underwater flight, eight-direction and seabed/overhead approaches, grace, surface escape and bounded relocation passed.");
    }

    private static void PhaseTimeline()
    {
        foreach (float dt in new[] { .01f, .05f, .1f })
        {
            using var world = new World(upload: false);
            var fish = world.Predator;
            var player = new Explorer { Position = new(96, -30, 120) };
            float regionDistance = 0, stalkDistance = 0;
            Check(Math.Abs(fish.Position.Y - SeaPredator.CruiseHeight) < .01f, "spawn close to the seabed");
            for (int i = 0; i < (int)Math.Round(25 / dt); i++)
            {
                double elapsed = (i + 1) * (double)dt;
                Check(!fish.Update(dt, world, player), "cannot swallow before the 25-second attack phase opens its mouth");
                var expected = elapsed + .00001 >= 25 ? PredatorState.Hunting : elapsed + .00001 >= 15 ? PredatorState.Stalking
                    : elapsed + .00001 >= 5 ? PredatorState.Approaching : PredatorState.Patrol;
                Check(fish.State == expected, $"exact phase boundary: dt={dt}, elapsed={elapsed}, state={fish.State}");
                Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "phase transitions remain collision-free");
                if (elapsed < 25 - .00001)
                {
                    Check(Math.Abs(fish.Position.Y - SeaPredator.CruiseHeight) <= .26f && Math.Abs(fish.Forward.Y) < .001f,
                        "all non-attacking phases stay level just above the seabed");
                    Check(fish.Gape <= .12f, "mouth stays mostly closed before the attack");
                }
                if (expected == PredatorState.Approaching)
                {
                    Check(fish.IntentTarget == new Vector3(96, SeaPredator.CruiseHeight, 96), "five-second phase targets the swimmer's open-water region");
                    regionDistance = Vector3.Distance(fish.Position, fish.IntentTarget);
                }
                if (expected == PredatorState.Stalking)
                {
                    Check(fish.IntentTarget == new Vector3(96, SeaPredator.CruiseHeight, 120), "fifteen-second phase targets below the swimmer");
                    stalkDistance = Vector3.Distance(fish.Position, fish.IntentTarget);
                }
            }
            Check(regionDistance < 190 && stalkDistance < 175, "both preparatory phases actually approach the swimmer");
            Check(fish.IntentTarget.Y > SeaPredator.CruiseHeight + 20, "only the attack aims upward at the swimmer");
            for (int i = 0; i < (int)Math.Round(3 / dt); i++)
                Check(!fish.Update(dt, world, player), "distant swimmer remains outside the mouth during ascent");
            Check(fish.Position.Y > SeaPredator.CruiseHeight + 2, "attacking fish can leave the seabed");
            player.Position = new(96, 0, 120);
            Check(!fish.Update(dt, world, player) && fish.State == PredatorState.Patrol && fish.DiveSeconds == 0 && !fish.TakingDetour,
                "surfacing clears all aggression and navigation");
            fish.Stage(new(96, -30, 96), Vector3.UnitZ, PredatorState.Hunting);
            for (int i = 0; i < (int)Math.Round(25 / dt); i++) fish.Update(dt, world, player);
            Check(Math.Abs(fish.Position.Y - SeaPredator.CruiseHeight) < .3f && Math.Abs(fish.Forward.Y) < .001f,
                "fish returns to level seabed cruising after the swimmer surfaces");

            world.EnsureAround(new(3840, -35, -3840), 0);
            fish.Stage(new(96, SeaPredator.CruiseHeight, 96), Vector3.UnitZ, PredatorState.Hunting);
            player.Position = new(3840, -35, -3840);
            fish.Update(dt, world, player);
            Check(fish.Active && fish.State == PredatorState.Hunting && fish.DiveSeconds >= 25, "streaming relocation preserves continuous dive progression");
        }
        Console.WriteLine("PREDATOR PHASES: exact distant 0/5/15/25-second transitions, deep level movement, ascent/return, reset and continuous-dive relocation passed at three frame rates.");
    }

    private static void ProximityAttacks()
    {
        using var world = new World(upload: false);
        var fish = world.Predator;
        foreach (float dt in new[] { .01f, .05f, .1f })
            foreach (var phase in new[] { PredatorState.Patrol, PredatorState.Approaching, PredatorState.Stalking })
                foreach (bool flying in new[] { false, true })
                {
                    fish.Stage(new(96, SeaPredator.CruiseHeight, 96), Vector3.UnitZ, phase);
                    var player = new Explorer { Position = new(96, SeaPredator.CruiseHeight + .5f, 120), Flying = flying };
                    Check(!fish.Update(dt, world, player) && fish.State == PredatorState.Hunting && fish.DiveSeconds < 25,
                        $"nearby swimmer triggers immediate attack in every preparatory phase: phase={phase}, dt={dt}, flying={flying}");
                    bool eaten = false;
                    for (int i = 0; i < (int)Math.Round(10 / dt); i++)
                    {
                        eaten = fish.Update(dt, world, player);
                        Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "proximity attack stays collision-free");
                        if (eaten) break;
                    }
                    Check(eaten && player.Position == world.Views[0].Position, "proximity attack actually swallows and restores the start");
                }
        var swimmer = new Explorer();
        fish.Stage(new(96, SeaPredator.CruiseHeight, 96), Vector3.UnitZ, PredatorState.Patrol);
        swimmer.Position = fish.Position + new Vector3(0, 0, SeaPredator.Proximity + 1);
        Check(!fish.Update(.01f, world, swimmer) && fish.State == PredatorState.Patrol, "outside the proximity radius still follows the timer");
        swimmer.Position = fish.Position + new Vector3(0, 0, SeaPredator.Proximity - 1);
        fish.Update(.01f, world, swimmer);
        Check(fish.State == PredatorState.Hunting && fish.DiveSeconds < 1, "inside the radius attacks during the first second");
        swimmer.Position = fish.Position + new Vector3(0, 0, 80);
        fish.Update(.05f, world, swimmer);
        Check(fish.State == PredatorState.Hunting && fish.DiveSeconds < 1, "leaving the trigger radius does not cancel an attack");
        swimmer.Position = new(swimmer.Position.X, World.WaterLevel + 1, swimmer.Position.Z);
        fish.Update(.05f, world, swimmer);
        Check(fish.State == PredatorState.Patrol && fish.DiveSeconds == 0, "surfacing still cancels a proximity attack");
        fish.Reset(world, world.Views[0].Position, 10);
        swimmer.Position = fish.Mouth + Vector3.UnitY * .9f;
        Check(!fish.Update(2, world, swimmer) && fish.State == PredatorState.Patrol, "respawn grace still prevents close-range attacks");
        fish.Stage(new(96, SeaPredator.CruiseHeight, 96), Vector3.UnitZ, PredatorState.Patrol);
        swimmer.Position = new(96, SeaPredator.CruiseHeight, 120);
        fish.Update(.01f, world, swimmer);
        swimmer.Position = new(3840, -35, -3840); world.EnsureAround(swimmer.Position, 0);
        fish.Update(.01f, world, swimmer);
        Check(fish.Active && fish.State == PredatorState.Hunting && fish.DiveSeconds < 1, "relocation preserves a proximity-triggered attack before 25 seconds");
        Console.WriteLine("PREDATOR PROXIMITY: immediate attacks in 3 phases, 3 frame rates and underwater flight; radius boundary, sustained pursuit, grace, surface reset and relocation passed.");
    }

    private static void WaterEntrySpawns()
    {
        foreach (int seed in new[] { 1701, 48291, -31 })
        {
            using var world = new World(upload: false, seed: seed, originX: seed == -31 ? 1_000_000_000_000 : 0,
                originZ: seed == -31 ? -1_000_000_000_000 : 0);
            var fish = world.Predator;
            foreach (var site in new Vector2[] { new(96, 0), new(0, 96), new(-96, 0), new(0, -96), new(96, 96), new(51, -95), new(-25, 100) })
            {
                var player = new Explorer { Position = new(site.X, World.WaterLevel + 1, site.Y) };
                fish.Stage(new(192, SeaPredator.CruiseHeight, 192), Vector3.UnitZ, PredatorState.Patrol);
                fish.Update(.01f, world, player);
                int count = fish.EntrySpawnCount;
                player.Position.Y = World.WaterLevel + .2f;
                fish.Update(.01f, world, player);
                Check(fish.EntrySpawnCount == count + 1 && fish.Active, "each entry prepares exactly one active fish before head immersion");
                Check(Math.Abs(fish.Position.Y - SeaPredator.CruiseHeight) < .05f && Planar(fish.Position, player.Position) < 200
                    && SeaPredator.Clear(world, fish.Position, fish.Forward), "entry placement is nearby, at the seabed, and clear of the whole body");
                for (int i = 0; i < 100; i++)
                {
                    player.Position.Y = World.WaterLevel + (i % 2 == 0 ? .05f : -.05f);
                    fish.Update(.01f, world, player);
                }
                Check(fish.EntrySpawnCount == count + 1, "waterline bobbing cannot repeatedly respawn fish");
                player.Position.Y = World.WaterLevel - 1;
                for (int i = 0; i < 510; i++) fish.Update(.01f, world, player);
                Check(fish.EntrySpawnCount == count + 1 && fish.DiveSeconds >= 5,
                    "continuous underwater movement preserves dive progression");
                player.Position.Y = World.WaterLevel + .6f; fish.Update(.01f, world, player);
                player.Position.Y = World.WaterLevel + .2f; fish.Update(.01f, world, player);
                Check(fish.EntrySpawnCount == count + 2 && fish.Active, "surfacing rearms the next entry");
            }
        }
        using (var world = new World(upload: false))
        {
            var floor = new Geometry();
            floor.Quad(new(-96, -59, -96), new(-96, -59, 96), new(96, -59, 96), new(96, -59, -96), Color.Gray);
            foreach (var sector in world.Sectors) floor.AddCollisionTo(sector.Collision);
            var fish = world.Predator;
            var player = new Explorer { Position = new(0, 1, 0) };
            fish.Reset(world, player.Position, waterEntry: true);
            Check(!fish.Active, "fully blocked spawn sites reproduce a failed placement");
            player.Position = new(3840, 1, -3840); world.EnsureAround(player.Position, 0);
            fish.Update(1.1f, world, player);
            Check(fish.Active && Planar(fish.Position, player.Position) < 200 && SeaPredator.Clear(world, fish.Position, fish.Forward),
                "inactive fish retries and recovers when the streamed neighborhood becomes available");
        }
        Console.WriteLine("PREDATOR ENTRY: 21 shoreline sites over 3 seeds, huge addresses, one seabed spawn per entry, bobbing/continuous-dive hysteresis, re-entry and failed-placement recovery passed.");
        static float Planar(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
    }

    private static void StartupPursuits()
    {
        int detours = 0;
        foreach (bool entrySpawn in new[] { false, true })
        foreach (int seed in new[] { 1701, 48291, -31 })
        {
            using var world = new World(upload: false, seed: seed);
            var fish = world.Predator; var player = new Explorer();
            foreach (var target in new Vector3[] { new(96, -35, 0), new(0, -35, 96), new(-96, -35, 0), new(0, -35, -96),
                new(-96, -35, 96), new(80, -30, 24), new(-25, -35, 100), new(0, -24, 96), new(0, World.SeaFloor + Explorer.EyeHeight, 96) })
            {
                // Exercise both the old distant patrol and actual fresh entry placement.
                fish.Reset(world, world.Views[0].Position);
                if (!entrySpawn) fish.Stage(fish.Position, fish.Forward, PredatorState.Patrol);
                player.GoTo(world.Views[0]); player.Position = target;
                bool eaten = false, detour = false, hunted = false;
                for (int i = 0; i < 1800; i++)
                {
                    eaten = fish.Update(.05f, world, player);
                    detour |= fish.TakingDetour; hunted |= fish.State == PredatorState.Hunting;
                    Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "startup detour keeps the full body clear");
                    if (eaten) break;
                }
                Check(hunted && eaten, $"real startup patrol can round the island and swallow within 90 seconds: seed={seed}, target={target}, fish={fish.Position}, state={fish.State}");
                Check(!fish.TakingDetour && player.Position == world.Views[0].Position, "swallow clears the route and restores the real start");
                if (detour) detours++;
            }
            fish.Reset(world, world.Views[0].Position);
            if (!entrySpawn) fish.Stage(fish.Position, fish.Forward, PredatorState.Patrol);
            player.Position = new(0, -35, 96);
            for (int i = 0; i < 400; i++) fish.Update(.05f, world, player);
            Check(fish.State == PredatorState.Stalking, "moving-target scenario begins during seabed stalking");
            player.Position = new(-96, -35, -96);
            bool caughtMovingTarget = false;
            for (int i = 0; i < 1800; i++)
            {
                caughtMovingTarget = fish.Update(.05f, world, player);
                Check(SeaPredator.Clear(world, fish.Position, fish.Forward), "replanned pursuit remains collision-free");
                if (caughtMovingTarget) break;
            }
            Check(caughtMovingTarget, "pursuit replans when the submerged target changes direction");
        }
        Check(detours >= 6, "actual startup scenarios exercise open-water detours");
        Console.WriteLine($"PREDATOR ROUTES: 54 distant-patrol/entry pursuits, {detours} detours, shallow/seabed targets and 6 moving-target replans passed.");
    }
}
