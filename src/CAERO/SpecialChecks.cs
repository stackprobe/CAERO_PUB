using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Raylib_cs;

namespace Caero;

internal static class SpecialChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Special island test failed: " + message); }

    internal static (long X, long Z) Address(int number)
    {
        long r = 0;
        while ((2 * r + 1) * (2 * r + 1) < number) r++;
        if (r == 0) return (0, 0);
        long d = (2 * r + 1) * (2 * r + 1) - number;
        return d < 2 * r ? (r - d, -r) : d < 4 * r ? (-r, -r + d - 2 * r)
            : d < 6 * r ? (-r + d - 4 * r, r) : (r, r - d + 6 * r);
    }

    internal static void Run()
    {
        var data = SpecialContent.Data;
        Check(data.Objects.Length == 49 && data.Objects.Select(d => d.Island).Distinct().Count() == 16, "all 49 authored additions on 16 islands");
        Check(data.Objects.Count(d => d.Kind == SpecialKind.Resident) == 29
            && data.Objects.Count(d => d.Kind == SpecialKind.Inscription) == 14
            && data.Objects.Count(d => d.Kind == SpecialKind.Item) == 5
            && data.Objects.Count(d => d.Kind == SpecialKind.Exit) == 1, "complete authoring manifest");
        Check(data.Objects.Single(d => d.Island == "31" && d.Kind == SpecialKind.Item).Effect == ItemEffect.Jump
            && data.Objects.Single(d => d.Island == "71" && d.Kind == SpecialKind.Item).Effect == ItemEffect.Dash,
            "islands 31 and 71 have exchanged upgrades");
        var rotations = new HashSet<int>(); int placements = 0;
        foreach (int seed in new[] { 1701, 48291, -31, 1, 2, 3, 4, 5 })
            foreach (var group in data.Objects.GroupBy(d => d.Island))
            {
                var (x, z) = Address(int.Parse(group.Key));
                Check(Sector.SpiralNumber(x, z).ToString() == group.Key, "authored number resolves to the correct spiral island");
                using var s = Sector.Generate(seed, x, z);
                using var baseline = Sector.Generate(seed, x, z, includeSpecial: false);
                using var repeat = Sector.Generate(seed, x, z);
                rotations.Add(s.Rotation);
                Check(s.Stone.Vertices.SequenceEqual(baseline.Stone.Vertices) && s.Land.Vertices.SequenceEqual(baseline.Land.Vertices)
                    && s.ProceduralPeople.Select(p => (p.Id, p.Anchor, p.Patrol)).SequenceEqual(baseline.People.Select(p => (p.Id, p.Anchor, p.Patrol))),
                    "authored additions preserve every base mesh vertex and procedural resident");
                Check(s.SpecialObjects.Count + s.People.Count(p => p.SpecialId != null) == group.Count(), "every configured entry placed exactly once");
                Check(s.SpecialObjects.Select(o => (o.Definition.Id, o.LocalFoot)).SequenceEqual(repeat.SpecialObjects.Select(o => (o.Definition.Id, o.LocalFoot)))
                    && s.People.Select(p => (p.SpecialId, p.Anchor)).SequenceEqual(repeat.People.Select(p => (p.SpecialId, p.Anchor))), "reproducible placements");
                foreach (var o in s.SpecialObjects)
                {
                    var support = o.Definition.Kind == SpecialKind.Exit ? s.Collision : baseline.Collision;
                    Check(Math.Abs(support.FloorAt(o.Foot.X, o.Foot.Z, o.Foot.Y + .01f) - o.Foot.Y) < .02f,
                        $"{o.Definition.Id} rests on its intended floor");
                    Check(o.Geometry.HasFiniteNormals && o.Geometry.VertexCount > 0, "finite visible special geometry");
                    if (o.Definition.Kind == SpecialKind.Item)
                        Check(o.PedestalGeometry.VertexCount > 0 && o.PedestalGeometry.HasFiniteNormals,
                            "potion has a separate permanent pedestal");
                    Check(o.Geometry.CountCoplanarOverlaps() == 0, "no overlapping coplanar ornament faces");
                    var foot = s.ResidentLocal(o.Foot);
                    if (o.Definition.Kind == SpecialKind.Exit)
                    {
                        Check(Vector3.Distance(foot, new(22, 6, 12.2f)) < .001f, "exit relic stands at the far wall on the first landing's extension");
                        Check(o.PedestalGeometry.VertexCount > 0 && o.PedestalGeometry.HasFiniteNormals,
                            "exit island has a separate supported corridor floor");
                        Check(o.Geometry.Vertices.Max(v => v.Y) - foot.Y > 2.6f, "exit uses the dedicated crystal geometry");
                        foreach (float side in new[] { -1f, 0, 1f })
                            for (float along = 15; along <= 31; along += .25f)
                            {
                                Vector3 eye = s.Transform(new(22 + side, 6 + Explorer.EyeHeight, along));
                                Check(!s.Collision.Blocked(eye) && PlacementSupport.HasFlatSupport(s.Collision,
                                    eye - Vector3.UnitY * Explorer.EyeHeight, new(.35f)), "extension is continuously walkable from the first landing");
                            }
                        Check(baseline.Collision.FloorAt(o.Foot.X, o.Foot.Z, 6.01f) < 1,
                            "extra corridor exists only with the exit island content");
                    }
                    float expectedHeight = o.Definition.Surface switch { SpecialSurface.HallRoof => s.HallHeight,
                        SpecialSurface.TowerLanding => o.Definition.Landing * 6, _ => 0 };
                    Check(Math.Abs(foot.Y - expectedHeight) < .01f, "surface height follows the authored floor or landing");
                    if (o.Definition.Island == "90263")
                    {
                        if (o.Definition.Surface == SpecialSurface.TowerLanding)
                            Check(Vector3.Distance(foot, new(15, expectedHeight, o.Definition.Landing == 2 ? 12.5f : 31.5f)) < .001f,
                                "purple inscriptions occupy the second and third stair landings exactly");
                        else if (seed == World.DefaultSeed && o.Definition.Id == "island-90263-03")
                            Check(Vector3.Distance(foot, new(22, 0, 28)) < .001f && s.TowerEntranceMask == 2
                                && Vector3.Dot(o.LocalFacing, s.Transform(-Vector3.UnitZ) - s.Offset) > .999f,
                                "red inscription is centered opposite the north entrance, facing the entering player");
                    }
                    placements++;
                }
                foreach (var p in s.People.Where(p => p.SpecialId != null))
                {
                    Check(p.Pose == ResidentPose.Sitting ? Residents.SittingSupported(s.Collision, p.Anchor, p.Scale, p.Yaw)
                        : Residents.Supported(s.Collision, p.Anchor, p.Anchor, p.Scale * .48f), "authored people neither float nor intersect walls");
                    var d = group.Single(d => d.Id == p.SpecialId);
                    Check(p.Huddled == d.Huddled, "authored huddled posture preserved");
                    if (d.MinResidentDistance > 0)
                        Check(s.People.Where(other => other != p && Math.Abs(other.Anchor.Y - p.Anchor.Y) < 2.5f)
                            .All(other => Vector2.Distance(new(other.Anchor.X, other.Anchor.Z), new(p.Anchor.X, p.Anchor.Z))
                                >= d.MinResidentDistance + Vector3.Distance(other.Anchor, other.Patrol)),
                            "isolated resident remains away from other people and their complete patrols");
                    if (d.LeanAgainstWall)
                    {
                        Vector3 back = -new Vector3(MathF.Sin(p.Yaw), 0, MathF.Cos(p.Yaw));
                        Vector3 across = new(back.Z, 0, -back.X);
                        foreach (float y in new[] { .30f, .65f })
                            foreach (float side in new[] { -.13f, .13f })
                            {
                                Vector3 point = p.Anchor + (Vector3.UnitY * y + across * side) * p.Scale;
                                Check(s.Collision.Obstructs(point, point + back * (.34f * p.Scale)),
                                    "seated resident has a real wall directly behind hips and shoulders");
                            }
                        Check(Math.Abs(s.ResidentLocal(p.Anchor).Y - s.HallHeight) < .001f,
                            "wall-backed resident remains on the hall roof");
                    }
                    if (d.Id == "island-1-06")
                    {
                        Vector3 foot = s.ResidentLocal(p.Anchor);
                        Check(p.Pose == ResidentPose.Sitting && Math.Abs(foot.Y - s.TowerFloors * 12) < .001f,
                            "new resident sits on the tower roof floor");
                        if (seed == World.DefaultSeed)
                        {
                            Vector3 center = s.Transform(new(22, foot.Y, 22));
                            Check(p.Anchor.X > center.X && p.Anchor.Z > center.Z,
                                "resident is in the southeast roof corner in the fixed world");
                        }
                        ChoiceBranches(p);
                    }
                    if (d.Id == "island-641-06")
                    {
                        Vector3 foot = s.ResidentLocal(p.Anchor);
                        Check(p.Pose == ResidentPose.Standing && Math.Abs(foot.Y - s.TowerFloors * 12) < .001f,
                            "island 641 observer stands at the very top of the walled tower");
                        if (seed == World.DefaultSeed)
                        {
                            var (targetX, targetZ) = Address(701);
                            Vector3 toward = new((targetX - x) * World.CellSize - (p.Anchor.X - s.Offset.X), 0,
                                (targetZ - z) * World.CellSize - (p.Anchor.Z - s.Offset.Z));
                            Check(Vector3.Dot(Vector3.Normalize(toward), new(MathF.Sin(p.Yaw), 0, MathF.Cos(p.Yaw))) > .999999f,
                                "fixed-seed island 641 observer faces island 701");
                        }
                    }
                    if (d.Id == "island-1169587-02")
                    {
                        Vector3 foot = s.ResidentLocal(p.Anchor);
                        Check(d.Surface == SpecialSurface.TowerInterior && foot.Y == 0,
                            "final island resident stands on the walled tower's first floor");
                        if (seed == World.DefaultSeed)
                        {
                            Vector3 center = s.Transform(new(22, 0, 22));
                            Check(p.Anchor.X > center.X + 7 && p.Anchor.Z < center.Z - 7,
                                "final island resident occupies the northeast end in the fixed world");
                        }
                    }
                    if (d.Id == "island-1-05")
                    {
                        Check(p.Pose == ResidentPose.Standing && Vector3.Distance(p.Anchor, new(36.3f, 2, 36.3f)) < .001f,
                            "island 1 observer stands at the outer corner of the raised platform for every rotation");
                        var (targetX, targetZ) = Address(3);
                        Vector3 toward = Vector3.Normalize(new Vector3(targetX * World.CellSize, 2, targetZ * World.CellSize) - p.Anchor);
                        Check(Vector3.Dot(toward, new(MathF.Sin(p.Yaw), 0, MathF.Cos(p.Yaw))) > .9999f,
                            "observer faces island 3 independently of island rotation");
                        Vector3 approach = p.Anchor - toward * 2.19f;
                        Check(PlacementSupport.HasFlatSupport(s.Collision, approach, new(.35f))
                            && !s.Collision.Obstructs(approach + Vector3.UnitY * Explorer.EyeHeight, p.LookPoint),
                            "observer can be approached and spoken to from behind");
                    }
                    Check(p.Speech.Pages.SequenceEqual(d.Pages) && p.Pose == d.Pose, "authored speech and requested sitting/standing pose");
                    var conversation = new Conversation(); conversation.Start(p);
                    for (int page = 0; page < p.Speech.Pages.Length; page++) { conversation.Update(100); conversation.Advance(); }
                    while (conversation.Active) { conversation.Update(100); conversation.Advance(); }
                    Check(!conversation.Active && !p.Talking, "arbitrary-length authored dialogue closes and releases speaker");
                    placements++;
                }
                Vector3 old = s.SpecialObjects.FirstOrDefault()?.Foot ?? Vector3.Zero;
                s.SetOrigin(x + 1, z);
                if (s.SpecialObjects.Count > 0) Check(s.SpecialObjects[0].Foot == old - Vector3.UnitX * World.CellSize, "props follow floating origin");
            }
        Check(rotations.Count == 4, "all island rotations covered");
        Check(ExitRelic.Brightness(0) > ExitRelic.Brightness(3) && ExitRelic.Brightness(3) > 0
            && Math.Abs(ExitRelic.Brightness(0) - ExitRelic.Brightness(6)) < .0001f
            && Math.Abs(ExitRelic.Brightness(0) - ExitRelic.Brightness(.01)) < .001f,
            "exit glow pulses smoothly over six seconds without going dark");
        LandingRoutes();
        ExitAlcoveRoute();
        InteractionAndProgress();
        InvalidSettings();
        EndingChecks.Run();
        Console.WriteLine($"SPECIAL: {placements} supported placements, all 4 rotations, base generation unchanged, exact texts/poses, E reading/pickups, one-time stacked abilities, flight unlock, persistence, revisit and ending passed.");
    }

    private static void LandingRoutes()
    {
        var (x, z) = Address(90263);
        using var world = new World(upload: false, originX: x, originZ: z);
        var sector = world.Sectors.Single(s => s.X == x && s.Z == z);
        var route = sector.Routes.Single(r => r.Name == "tower switchbacks").Points.Select(world.AddressOf).ToArray();
        ProceduralChecks.Walk(world, "90263 tower ascent past the purple inscriptions", route);
        ProceduralChecks.Walk(world, "90263 tower descent past the purple inscriptions", route.Reverse().ToArray());
        Console.WriteLine("SPECIAL LANDINGS: island 90263 tower ascent/descent passed without jumping or flight.");
    }

    private static void ExitAlcoveRoute()
    {
        var (x, z) = Address(1169587);
        using var world = new World(upload: false, originX: x, originZ: z);
        var sector = world.Sectors.Single(s => s.X == x && s.Z == z);
        Vector3[] local = [new(18, 0, 13), new(18, 6, 31), new(22, 6, 31),
            new(22, 6, 27), new(22, 6, 20), new(22, 6, 14.5f)];
        var route = local.Select(sector.Transform).Select(world.AddressOf).ToArray();
        ProceduralChecks.Walk(world, "exit island first stair and extended corridor", route);
        ProceduralChecks.Walk(world, "exit island return from the relic", route.Reverse().ToArray());
        Console.WriteLine("EXIT CORRIDOR: first stair, landing, extended floor and return passed without jumping or flight.");
    }

    private static void ChoiceBranches(Resident person)
    {
        var choice = person.Speech.Choice!;
        var conversation = new Conversation();
        void Press(KeyboardKey key) => ConversationInput.Handle(conversation, candidate => candidate == key);
        static IEnumerable<bool[]> Paths(DialogueChoice current)
        {
            foreach (bool yes in new[] { true, false })
            {
                var next = yes ? current.YesChoice : current.NoChoice;
                if (next == null) yield return [yes];
                else foreach (var tail in Paths(next)) yield return [yes, .. tail];
            }
        }
        foreach (var answers in Paths(choice))
        foreach (var confirm in new[] { KeyboardKey.E, KeyboardKey.Space, KeyboardKey.Enter })
        {
            conversation.Start(person);
            conversation.SelectAnswer(false);
            Check(conversation.SelectedYes && !conversation.AwaitingChoice, "answers cannot change before the question");
            for (int page = 0; page < person.Speech.Pages.Length - 1; page++)
            { conversation.Update(100); conversation.Advance(); }
            Check(!conversation.AwaitingChoice, "choice waits for the question text to finish");
            Press(confirm);
            Check(conversation.AwaitingChoice && conversation.Page == person.Speech.Pages.Length - 1,
                "reveal exposes the choice without confirming an answer");
            conversation.Update(Conversation.ChoiceInputDelay);
            foreach (var key in new[] { KeyboardKey.Right, KeyboardKey.Down, KeyboardKey.D, KeyboardKey.S })
            {
                Press(KeyboardKey.Left); Press(key);
                Check(!conversation.SelectedYes, "right/down/D/S choose No through the gameplay input handler");
            }
            foreach (var key in new[] { KeyboardKey.Left, KeyboardKey.Up, KeyboardKey.A, KeyboardKey.W })
            {
                Press(KeyboardKey.Right); Press(key);
                Check(conversation.SelectedYes, "left/up/A/W choose Yes through the gameplay input handler");
            }
            var current = choice;
            foreach (bool yes in answers)
            {
                Check(conversation.AwaitingChoice && conversation.SelectedYes, "each nested question starts with Yes selected");
                conversation.Update(Conversation.ChoiceInputDelay);
                Press(yes ? KeyboardKey.Left : KeyboardKey.Right); Press(confirm);
                string[] expected = yes ? current.YesPages : current.NoPages;
                var next = yes ? current.YesChoice : current.NoChoice;
                for (int page = 0; page < expected.Length; page++)
                {
                    Check(conversation.Active && conversation.Text == expected[page] && !conversation.AwaitingChoice,
                        "only the selected branch's pages are shown, with choices hidden until text is revealed");
                    conversation.Update(100);
                    if (next != null && page == expected.Length - 1)
                        Check(conversation.AwaitingChoice, "next choice follows the selected branch's final page");
                    else Press(confirm);
                }
                current = next!;
            }
            Check(!conversation.Active && !person.Talking, "both answer branches release the speaker");
        }
        conversation.Start(person);
        for (int page = 0; page < person.Speech.Pages.Length - 1; page++)
        { conversation.Update(100); conversation.Advance(); }
        conversation.Update(100); Press(KeyboardKey.Right); Press(KeyboardKey.Escape);
        Check(!conversation.AwaitingChoice && !person.Talking, "cancel clears the pending choice");
        conversation.Start(person);
        Check(conversation.SelectedYes && conversation.Page == 0, "reopening resets the selected answer and page");
        conversation.Close();
        if (choice.NoChoice != null)
        {
            conversation.Start(person);
            for (int page = 0; page < person.Speech.Pages.Length - 1; page++)
            { conversation.Update(100); conversation.Advance(); }
            conversation.Update(100); conversation.Update(Conversation.ChoiceInputDelay); Press(KeyboardKey.Right); Press(KeyboardKey.Enter);
            for (int page = 0; page < choice.NoPages.Length - 1; page++)
            { conversation.Update(100); conversation.Advance(); }
            conversation.Update(100); Press(KeyboardKey.Right); Press(KeyboardKey.Escape);
            Check(!conversation.Active && !person.Talking, "Escape closes a nested question and releases the speaker");
            conversation.Start(person);
            Check(conversation.Page == 0 && conversation.SelectedYes, "reopening after nested cancellation restarts from the beginning");
            conversation.Close();
        }
    }

    private static void InteractionAndProgress()
    {
        using var world = new World(upload: false);
        var player = new Explorer(); var session = new Conversation();
        float dataDash = SpecialContent.Data.DashBonusPerItem, dataJump = SpecialContent.Data.JumpBonusPerItem;
        Check(!world.Progress.CanFly && world.Progress.DashBonus == 0 && world.Progress.JumpMultiplier == 1, "new adventures begin without upgrades or flight");
        foreach (var group in SpecialContent.Data.Objects.GroupBy(d => d.Island).OrderBy(g => g.First().Kind == SpecialKind.Exit))
        {
            var (x, z) = Address(int.Parse(group.Key));
            world.Teleport(player, new(x, z, new Vector3(0, Explorer.EyeHeight, 0)), radius: 0);
            var sector = world.Sectors.Single(s => s.X == x && s.Z == z);
            foreach (var seated in sector.People.Where(p => p.SpecialId != null && SpecialContent.ById[p.SpecialId].LeanAgainstWall))
            {
                Vector3 approach = seated.Position + new Vector3(MathF.Sin(seated.Yaw), 0, MathF.Cos(seated.Yaw)) * 2.19f;
                Vector3 eye = approach + Vector3.UnitY * Explorer.EyeHeight;
                Check(PlacementSupport.HasFlatSupport(sector.Collision, approach, new(.35f))
                    && ReferenceEquals(Conversation.Target(world, eye, Vector3.Normalize(seated.LookPoint - eye)), seated),
                    "wall-backed resident can be approached and spoken to from the roof");
            }
            if (group.Key == "1")
            {
                var observer = sector.People.Single(p => p.SpecialId == "island-1-05");
                Vector3 forward = new(MathF.Sin(observer.Yaw), 0, MathF.Cos(observer.Yaw));
                Vector3 eye = observer.Position - forward * 2.19f + Vector3.UnitY * Explorer.EyeHeight;
                Check(ReferenceEquals(Conversation.Target(world, eye, Vector3.Normalize(observer.LookPoint - eye)), observer),
                    "E targets the island 3 observer from behind on the platform");
                var seated = sector.People.Single(p => p.SpecialId == "island-1-06");
                Vector3 approach = seated.Position + new Vector3(MathF.Sin(seated.Yaw), 0, MathF.Cos(seated.Yaw)) * 2.19f;
                Vector3 seatedEye = approach + Vector3.UnitY * Explorer.EyeHeight;
                Check(PlacementSupport.HasFlatSupport(sector.Collision, approach, new(.35f))
                    && ReferenceEquals(Conversation.Target(world, seatedEye, Vector3.Normalize(seated.LookPoint - seatedEye)), seated),
                    "seated roof resident is reachable for conversation from the roof");
            }
            foreach (var prop in sector.SpecialObjects)
            {
                player.Position = prop.Foot + prop.LocalFacing * 2.19f + Vector3.UnitY * Explorer.EyeHeight;
                Vector3 direction = Vector3.Normalize(prop.ExaminePoint - player.Position);
                player.Yaw = MathF.Atan2(direction.X, -direction.Z); player.Pitch = MathF.Asin(direction.Y);
                Check(!world.Blocked(player.Position), $"clear examination approach for {prop.Definition.Id}");
                Check(ReferenceEquals(SpecialInteraction.Target(world, player.Position, player.Forward), prop), $"E reaches {prop.Definition.Id} without being blocked by its own monument");
                Check(SpecialInteraction.Target(world, player.Position, -player.Forward) != prop, "objects behind player cannot activate");
                if (prop.Definition.Kind != SpecialKind.Item)
                {
                    Vector3 back = prop.Foot - prop.LocalFacing * 2.19f + Vector3.UnitY * Explorer.EyeHeight;
                    Check(SpecialInteraction.Target(world, back, Vector3.Normalize(prop.ExaminePoint - back)) != prop,
                        "a monument's solid back blocks reading through it");
                }
                SpecialInteraction.Examine(world, player, prop, session);
                if (prop.Definition.Kind == SpecialKind.Item)
                {
                    string color = prop.Definition.Effect switch { ItemEffect.Dash => "青い", ItemEffect.Jump => "赤い", _ => "緑の" };
                    Check(session.AwaitingChoice && session.Text == color + "薬がある。\n飲んでみますか？"
                        && !world.Progress.Collected(prop.Definition.Id) && !player.ConsumePotionDrink(),
                        "color-specific question precedes collection, abilities and drink sound");
                    session.SelectAnswer(false); session.Advance();
                    Check(!session.Active && !world.Progress.Collected(prop.Definition.Id) && !player.ConsumePotionDrink(),
                        "No leaves the potion uncollected and silent");
                    SpecialInteraction.Examine(world, player, prop, session); session.Close();
                    Check(!world.Progress.Collected(prop.Definition.Id), "closing the question does not drink");
                    int collections = 0;
                    SpecialInteraction.Examine(world, player, prop, session, () => collections++); session.Advance();
                    Check(collections == 1 && session.Document == prop && !session.AwaitingChoice,
                        "Yes fires acquisition callback and opens the original result message");
                    Check(world.Progress.Collected(prop.Definition.Id), "pickup records stable ID");
                    Check(player.ConsumePotionDrink() && !player.ConsumePotionDrink(), "one drink event per pickup");
                    float before = world.Progress.DashBonus + world.Progress.JumpMultiplier;
                    SpecialInteraction.Examine(world, player, prop, session);
                    Check(!player.ConsumePotionDrink(), "repeated pickup cannot replay the drink sound");
                    Check(before == world.Progress.DashBonus + world.Progress.JumpMultiplier, "repeat E cannot duplicate an ability");
                }
                else if (prop.Definition.Kind == SpecialKind.Exit)
                {
                    Check(!world.Progress.Escaped && session.AwaitingChoice
                        && session.Text == "碑石は輝いている。\n触れてみますか？", "exit asks before triggering the ending");
                    session.SelectAnswer(false); session.Advance();
                    Check(!world.Progress.Escaped && !session.Active, "No leaves the adventure playable");
                    SpecialInteraction.Examine(world, player, prop, session);
                    session.Close();
                    Check(!world.Progress.Escaped, "closing the question cannot escape");
                    SpecialInteraction.Examine(world, player, prop, session); session.Advance();
                    Check(world.Progress.Escaped && !session.Active, "Yes triggers the ending");
                    var goalSave = SaveStore.Capture(player, world);
                    Check(goalSave.Progress is { Escaped: false, ExitId: null }
                        && goalSave.Progress.Collected.SequenceEqual(world.Progress.Snapshot().Collected),
                        "goal save retains inventory without locking Continue into the ending");
                    Check(Vector3.Distance(player.Position, prop.Foot + prop.LocalFacing * 2.19f
                        + Vector3.UnitY * Explorer.EyeHeight) < .001f
                        && Vector3.Dot(player.Forward, Vector3.Normalize(prop.ExaminePoint - player.Position)) > .999f,
                        "goal saves just before the relic facing it");
                }
                else Check(session.Active && session.Document == prop && session.PageCount > 0, "E opens readable inscription pages");
                if (prop.Definition.Kind != SpecialKind.Exit)
                {
                    Check(session.PageCount == prop.Definition.Pages.Length, "authored document page count preserved");
                    foreach (string page in prop.Definition.Pages)
                    {
                        Check(session.Active && session.Text == page, "document page boundaries and line breaks preserved");
                        session.Update(1000); session.Advance();
                    }
                    Check(!session.Active, "document closes after the last authored page");
                }
                session.Close();
            }
        }
        Check(world.Progress.CanFly && world.Progress.DashBonus == dataDash * 2
            && Math.Abs(world.Progress.JumpMultiplier - (1 + dataJump * 2)) < .0001f, "both dash and jump upgrades stack; island 57 unlocks flight");
        var saved = new SavedView(0, 2, 0, 0, 0, false, Progress: world.Progress.Snapshot());
        var loaded = JsonSerializer.Deserialize<SavedView>(JsonSerializer.Serialize(saved));
        var progress = new AdventureProgress(); progress.Restore(loaded!.Progress);
        Check(progress.CanFly && progress.Escaped && progress.EndingText == EndingSequence.Message && progress.DashBonus == world.Progress.DashBonus
            && progress.JumpMultiplier == world.Progress.JumpMultiplier && progress.Snapshot().Collected.Length == 5, "save round trip retains every pickup and ending");
        progress.Restore(null); Check(!progress.CanFly && !progress.Escaped && progress.DashBonus == 0, "legacy saves start with default progression");
        var item = SpecialContent.Data.Objects.First(d => d.Kind == SpecialKind.Item);
        world.Teleport(player, new(Address(int.Parse(item.Island)).X, Address(int.Parse(item.Island)).Z, Vector3.Zero), radius: 0);
        Check(world.Sectors.SelectMany(s => s.SpecialObjects).Where(o => o.Definition.Id == item.Id).All(o => world.Progress.Collected(o.Definition.Id)), "regenerated pickups remain collected after revisit");
        // Exercise actual movement, not just inventory counters.
        world.Teleport(player, new(0, 0, new Vector3(80, Explorer.EyeHeight, 0)), radius: 0);
        player.Advance(.1f, world, Vector3.UnitX, false, false, false, true);
        Check(Math.Abs(player.Position.X - 80 - (13 + dataDash * 2) * .1f) < .002f, "dash pickups change real movement speed");
        var jump = new Explorer { Position = new(0, Explorer.EyeHeight, 0) };
        float peak = jump.Position.Y;
        jump.Advance(.01f, world, Vector3.Zero, true, true, false, false);
        for (int i = 0; i < 150; i++) { jump.Advance(.01f, world, Vector3.Zero, false, false, false, false); peak = Math.Max(peak, jump.Position.Y); }
        Check(peak - Explorer.EyeHeight > 2.1f, "stacked jump pickups increase actual jump apex");
    }

    private static void InvalidSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), "caero-special-check-" + Guid.NewGuid() + ".json");
        try
        {
            string valid = ContentAssets.ReadText(SpecialContent.FilePath);
            void RejectPages(JsonNode? pages, bool remove = false)
            {
                var root = JsonNode.Parse(valid)!;
                var entry = root["objects"]![0]!.AsObject();
                if (remove) entry.Remove("pages"); else entry["pages"] = pages;
                File.WriteAllText(path, root.ToJsonString());
                bool rejected = false;
                try { SpecialContent.Load(path); } catch (InvalidDataException ex) { rejected = ex.Message.Contains(path); }
                Check(rejected, "missing, empty, malformed or oversized pages identify the settings file");
            }
            RejectPages(null, remove: true);
            RejectPages(null);
            RejectPages(new JsonArray());
            RejectPages(new JsonArray("valid", " \n"));
            RejectPages(new JsonArray(JsonValue.Create("valid"), null));
            RejectPages(JsonValue.Create("not an array"));
            RejectPages(new JsonArray("tab\tcharacter"));
            RejectPages(new JsonArray(new string('a', 10001), new string('b', 10000)));
            void RejectChoice(JsonObject choice)
            {
                var root = JsonNode.Parse(valid)!;
                root["objects"]![0]!["choice"] = choice;
                File.WriteAllText(path, root.ToJsonString());
                bool rejected = false;
                try { SpecialContent.Load(path); } catch (InvalidDataException ex) { rejected = ex.Message.Contains(path); }
                Check(rejected, "invalid choice branches identify the settings file");
            }
            RejectChoice(new JsonObject { ["yesPages"] = new JsonArray() });
            RejectChoice(new JsonObject { ["yesPages"] = null, ["noPages"] = new JsonArray() });
            RejectChoice(new JsonObject { ["yesPages"] = new JsonArray(), ["noPages"] = new JsonArray(" ") });
            RejectChoice(new JsonObject { ["yesPages"] = new JsonArray(), ["noPages"] = new JsonArray(JsonValue.Create("valid"), null) });
            RejectChoice(new JsonObject { ["yesPages"] = new JsonArray(), ["noPages"] = new JsonArray("bad\tpage") });
            RejectChoice(new JsonObject { ["yesPages"] = new JsonArray(), ["noPages"] = new JsonArray(new string('a', 20001)) });
            RejectChoice(new JsonObject { ["yesPages"] = new JsonArray(new string('a', 10000)), ["noPages"] = new JsonArray(new string('b', 10000)) });
            RejectChoice(new JsonObject { ["yesPages"] = new JsonArray(), ["noPages"] = new JsonArray(),
                ["noChoice"] = new JsonObject { ["yesPages"] = new JsonArray("Yes"), ["noPages"] = new JsonArray("No") } });
            RejectChoice(new JsonObject { ["yesPages"] = new JsonArray("question"), ["noPages"] = new JsonArray(),
                ["yesChoice"] = new JsonObject { ["yesPages"] = new JsonArray(" "), ["noPages"] = new JsonArray() } });

            var authored = JsonNode.Parse(valid)!;
            string[] pages = [new string('あ', 90) + "\n改行も保持", "次のページ"];
            authored["objects"]![0]!["pages"] = new JsonArray(pages.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
            File.WriteAllText(path, authored.ToJsonString());
            var definition = SpecialContent.Load(path).Objects[0];
            using var document = new SpecialObject { Definition = definition, LocalFoot = Vector3.Zero,
                LocalFacing = Vector3.UnitZ, Geometry = new() };
            var conversation = new Conversation(); conversation.Read(document);
            Check(conversation.PageCount == 2 && conversation.Text == pages[0], "authored long page is not split automatically");
            conversation.Advance();
            Check(conversation.Page == 0 && conversation.Revealed == pages[0].Length, "first advance reveals the current page");
            conversation.Advance();
            Check(conversation.Page == 1 && conversation.Text == pages[1], "next advance selects exactly the next authored page");
            conversation.Update(100); conversation.Advance();
            Check(!conversation.Active, "authored page sequence closes after the last page");
            authored["objects"]![0]!["choice"] = new JsonObject { ["yesPages"] = new JsonArray(), ["noPages"] = new JsonArray() };
            File.WriteAllText(path, authored.ToJsonString());
            using var emptyBranches = new SpecialObject { Definition = SpecialContent.Load(path).Objects[0],
                LocalFoot = Vector3.Zero, LocalFacing = Vector3.UnitZ, Geometry = new() };
            foreach (bool yes in new[] { true, false })
            {
                conversation.Read(emptyBranches); conversation.Update(100); conversation.Advance(); conversation.Update(100);
                Check(conversation.AwaitingChoice, "empty branches still show the question");
                conversation.SelectAnswer(yes); conversation.Advance();
                Check(!conversation.Active && !conversation.AwaitingChoice, "empty answer branch closes immediately");
            }
            var nested = new DialogueChoice { YesPages = ["Yes question"], NoPages = ["No question"],
                YesChoice = new() { YesPages = ["Yes / Yes"], NoPages = ["Yes / No"] },
                NoChoice = new() { YesPages = ["No / Yes"], NoPages = ["No / No"] } };
            Check(nested.IsValid && nested.AllPages.Count() == 6, "nested validation and font collection include both subtrees");
            using var branching = new SpecialObject { Definition = definition with { Pages = ["First question"], Choice = nested },
                LocalFoot = Vector3.Zero, LocalFacing = Vector3.UnitZ, Geometry = new() };
            foreach (bool first in new[] { true, false })
            foreach (bool second in new[] { true, false })
            {
                conversation.Read(branching); conversation.Update(100);
                conversation.SelectAnswer(first); conversation.Advance();
                conversation.SelectAnswer(false);
                Check(!conversation.AwaitingChoice && conversation.SelectedYes, "next question resets the answer and waits for its text");
                conversation.Advance();
                Check(conversation.AwaitingChoice, "revealing the next question does not accidentally confirm it");
                conversation.SelectAnswer(second); conversation.Advance();
                Check(conversation.Text == $"{(first ? "Yes" : "No")} / {(second ? "Yes" : "No")}",
                    "all four nested answer paths reach their own final message");
                conversation.Update(100); conversation.Advance();
                Check(!conversation.Active, "nested leaf closes normally");
            }
            foreach (string invalid in new[] { valid.Replace("\"version\": 1", "\"version\": 99"),
                valid.Replace("\"landing\": 2", "\"landing\": 0"), valid.Replace("\"landing\": 2", "\"landing\": 99"),
                valid.Replace("island-1-02", "island-1-01"), valid.Replace("\"towerInterior\"", "\"typo\"") })
            {
                File.WriteAllText(path, invalid); bool rejected = false;
                try { SpecialContent.Load(path); } catch (InvalidDataException ex) { rejected = ex.Message.Contains(path); }
                Check(rejected, "bad versions, duplicate IDs and unknown placement values identify the settings file");
            }
        }
        finally { File.Delete(path); }
    }
}
