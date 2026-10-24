using System.Numerics;

namespace Caero;

internal static class FlightChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Flight test failed: " + message); }

    public static void Run()
    {
        using var world = new World(upload: false);
        var sector = world.Sectors.First(s => s.X == 0 && s.Z == 0);
        FlightControls(world);
        FlightInteractions(world);
        foreach (float dt in new[] { .01f, .1f })
        {
            var wall = new Explorer { Flying = true, Position = sector.Transform(new(8, 4, 22)) };
            Vector3 across = Vector3.Normalize(sector.Transform(Vector3.UnitX) - sector.Transform(Vector3.Zero));
            for (int i = 0; i < 100 && wall.Flying; i++)
                wall.Advance(dt, world, across, false, false, false, true);
            Check(!wall.Flying && wall.FlightBumped && !world.Blocked(wall.Position), "fast flight stops before tower wall");
            Check(!wall.FlightStarted && !wall.FlightStopped, "wall collision does not emit manual flight sounds");
            wall.Advance(dt, world, Vector3.Zero, false, false, false, false);
            Check(!wall.FlightBumped, "bump plays once");

            var floor = new Explorer { Flying = true, Position = sector.Transform(new(0, 5, 50)) };
            for (int i = 0; i < 100 && floor.Flying; i++)
                floor.Advance(dt, world, -Vector3.UnitY, false, false, false, true);
            Check(!floor.Flying && floor.FlightBumped && floor.Position.Y >= Explorer.EyeHeight + .2f
                && !world.Blocked(floor.Position), "floor contact leaves clearance");

            float Dive(bool fast)
            {
                var e = new Explorer { Flying = true, Position = new(90, World.WaterLevel + Explorer.EyeHeight + 2, 90) };
                for (int i = 0; i < 100 && e.Flying; i++)
                    e.Advance(dt, world, -Vector3.UnitY, false, false, false, fast);
                Check(!e.Flying && e.Swimming && e.SplashImpact > 0 && !e.FlightBumped, "water contact uses splash");
                Check(!e.FlightStarted && !e.FlightStopped, "water contact does not emit manual flight sounds");
                float min = e.Position.Y;
                for (int i = 0; i < (int)(4 / dt); i++)
                {
                    e.Advance(dt, world, Vector3.Zero, false, false, false, false);
                    min = Math.Min(min, e.Position.Y);
                }
                return min;
            }
            Check(Dive(true) < Dive(false) - 2, "faster flight impact sinks deeper");
        }
        var clip = AudioSynthesis.Create().Single(c => c.Name == "bump");
        Check(clip.Samples.All(float.IsFinite) && clip.Samples.Max(Math.Abs) is > .05f and < .95f,
            "bump sound has finite audible samples with headroom");
        Console.WriteLine("FLIGHT PASS: wall/floor clearance, fast flight collision, single bump, splash and impact-dependent sinking.");
    }

    private static void FlightControls(World world)
    {
        var player = new Explorer { Position = world.Views[0].Position };
        player.Advance(.02f, world, Vector3.Zero, false, false, false, false);
        player.Advance(.1f, world, Vector3.Zero, true, true, false, false);
        Check(!player.ToggleFlight(world), "jumping without the flight potion cannot enter flight");
        world.Progress.Collect(SpecialContent.Data.Objects.Single(d => d.Effect == ItemEffect.Flight));
        player.GoTo(world.Views[0]); player.Advance(.02f, world, Vector3.Zero, false, false, false, false);
        player.AdvanceControls(.02f, world, Vector3.Zero, false, false, false, false, true);
        Check(!player.Flying && !player.FlightStarted && !player.FlightStopped, "F on the ground emits no flight sound");
        player.AdvanceControls(.1f, world, Vector3.Zero, true, true, false, false, false);
        Check(player.Jumped && !player.Grounded, "takeoff requires an actual jump");
        Vector3 start = player.Position, direction = player.Forward;
        player.AdvanceControls(.1f, world, -direction, false, false, false, false, true);
        Check(player.Flying && Vector3.Distance(player.Position, start + direction * .5f) < .002f,
            "F during a jump starts flight and backward input cannot reverse it");
        Check(player.FlightStarted && !player.FlightStopped && !player.FlightBumped, "F takeoff emits only the start sound");
        start = player.Position;
        player.AdvanceControls(.1f, world, Vector3.Zero, false, false, false, false, false);
        Check(Vector3.Distance(player.Position, start + direction * .5f) < .002f, "flight continues without movement keys");
        Check(!player.FlightStarted && !player.FlightStopped, "manual flight sounds reset on the next frame");
        player.Yaw += .4f; player.Pitch = .25f; start = player.Position;
        player.AdvanceControls(.1f, world, -player.Forward, false, false, true, false, false);
        Check(Vector3.Distance(player.Position, start + player.Forward * .5f) < .002f, "look direction steers flight; descend input cannot override it");
        player.AdvanceControls(.02f, world, Vector3.Zero, false, false, false, false, true);
        Check(!player.Flying && player.FlightStopped && !player.FlightStarted && !player.FlightBumped,
            "F cancellation emits only the manual stop sound");
        player.AdvanceControls(.02f, world, Vector3.Zero, false, false, false, false, true);
        Check(!player.Flying && !player.FlightStarted && !player.FlightStopped, "F while falling cannot restart flight or repeat the stop sound");
        player.GoTo(new("FALL", new(80, 40, 0), new(80, 40, -1)));
        player.Advance(.02f, world, Vector3.Zero, false, false, false, false);
        Check(!player.ToggleFlight(world), "walking off a ledge is not a jump");
        player.GoTo(world.Views[0]); player.Advance(.02f, world, Vector3.Zero, false, false, false, false);
        player.Advance(.02f, world, Vector3.Zero, true, true, false, false);
        for (int i = 0; i < 100; i++) player.Advance(.02f, world, Vector3.Zero, false, false, false, false);
        Check(player.Grounded && !player.ToggleFlight(world), "landing clears jump eligibility");
        world.Progress.Restore(null);
        Console.WriteLine("FLIGHT INPUT: potion gating, jump-only takeoff, constant forward motion, mouse steering, cancellation and landing reset passed.");
    }

    private static void FlightInteractions(World world)
    {
        var player = new Explorer(); var conversation = new Conversation();
        foreach (var prop in world.Sectors.SelectMany(s => s.SpecialObjects)
            .Where(o => o.Definition.Kind is SpecialKind.Item or SpecialKind.Inscription))
        {
            player.GoTo(new("FLIGHT INTERACTION", prop.Foot + prop.LocalFacing * 2.19f + Vector3.UnitY * Explorer.EyeHeight,
                prop.ExaminePoint));
            player.Flying = true;
            SpecialInteraction.Examine(world, player, prop, conversation);
            Check(!conversation.Active && !world.Progress.Collected(prop.Definition.Id), "flight cannot read inscriptions or collect items");
            if (prop.Definition.Kind != SpecialKind.Inscription) continue;
            player.Flying = false;
            SpecialInteraction.Examine(world, player, prop, conversation);
            Check(conversation.Document == prop, "leaving flight restores inscription interactions");
            conversation.Close();
        }
        using var distant = Sector.Generate(1701, 15, 10);
        var terminal = new WarpTarget(distant, distant.WarpTerminals.First());
        player.Flying = true;
        Check(!WarpTravel.Examine(world, player, terminal, conversation) && !conversation.Active && !world.WarpInProgress,
            "flight cannot open a warp confirmation");
        Console.WriteLine("FLIGHT INTERACTIONS: inscriptions, pickups and warp confirmations blocked in flight; reading restored after flight.");
    }
}
