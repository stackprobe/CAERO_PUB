using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class ResidentChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Resident check failed: " + message); }
    public static void Run()
    {
        DialogueChecks.Run();
        CollisionChecks();
        using var origin = Sector.Generate(1701, 0, 0);
        var populations = new HashSet<int>();
        var poses = new HashSet<ResidentPose>();
        var rotations = new HashSet<int>(); int count = 0, backrests = 0;
        foreach (var (seed, x, z) in new[] { (1701, 0, 0), (1701, 1, 0), (1701, -3, -4), (1701, 15, 10),
            (1701, -54, -16), (1701, -4, -16), (1, 8, -4), (2, 8, -4), (3, 8, -4), (4, 8, -4) })
        {
            using var a = Sector.Generate(seed, x, z); using var b = Sector.Generate(seed, x, z);
            rotations.Add(a.Rotation);
            Check(a.People.Select(p => (p.Id, p.Profile, p.Pose, p.Anchor, p.Patrol, p.Scale, p.Yaw)).SequenceEqual(b.People.Select(p => (p.Id, p.Profile, p.Pose, p.Anchor, p.Patrol, p.Scale, p.Yaw))), "stable regeneration and fixed dialogue without altering map random sequence");
            Check(a.ProceduralPeople.Count() is >= 0 and <= 5, "zero to five residents on every island");
            populations.Add(a.ProceduralPeople.Count());
            foreach (var p in a.ProceduralPeople)
                foreach (var q in a.ProceduralPeople.Where(q => q.Id != p.Id))
                    Check(Vector3.Distance(p.Anchor, q.Anchor) - Vector3.Distance(p.Anchor, p.Patrol)
                        - Vector3.Distance(q.Anchor, q.Patrol) >= 15, "patrols stay at least fifteen units apart");
            foreach (var p in a.ProceduralPeople)
            {
                var speech = Dialogue.Lines[p.Profile];
                Check(p.Age == speech.Age && p.Pose == speech.Pose && p.Female == (speech.Gender == "female"),
                    "external dialogue metadata matches the rendered speaker");
                count++;
                poses.Add(p.Pose);
                if (p.Pose == ResidentPose.Sitting)
                {
                    Vector3 back = p.Anchor + Vector3.UnitY * (.65f * p.Scale);
                    Vector3 behind = new(-MathF.Sin(p.Yaw), 0, -MathF.Cos(p.Yaw));
                    if (a.Collision.Obstructs(back, back + behind * (.34f * p.Scale))) backrests++;
                }
                float radius = p.Scale * .48f;
                Check(a.ResidentAreas.Any(area => Enumerable.Range(0, 11).All(step =>
                    Residents.AllowedFootprint(a, area, Vector3.Lerp(p.Anchor, p.Patrol, step / 10f), p.Scale, p.Pose, p.Yaw))),
                    "entire body and patrol stay on eligible floors and outside enclosed buildings");
                Check(p.Pose == ResidentPose.Sitting ? Residents.SittingSupported(a.Collision, p.Anchor, p.Scale, p.Yaw)
                    : Residents.Supported(a.Collision, p.Anchor, p.Patrol, radius), "whole body and patrol have a clear supported footprint");
                for (int i = 0; i < 1200; i++)
                {
                    p.Update(.05f);
                    Check(Vector3.Distance(p.Position, p.Anchor) <= 2.501f && float.IsFinite(p.Yaw), "slow walking stays inside validated path");
                    Check(Math.Abs(a.Collision.FloorAt(p.Position.X, p.Position.Z, p.Position.Y + .05f) - p.Position.Y) < .04f, "no floating or falling through floors");
                }
                if (p.Pose != ResidentPose.Walking) Check(p.Position == p.Anchor, "sitting and standing people stay in place");
                var session = new Conversation(); var position = p.Position;
                session.Start(p); p.Update(1);
                Check(p.Talking && p.Position == position, "speaker stops while talking");
                session.Advance(); Check(session.Revealed == session.Text.Length && session.Page == 0, "first press reveals remaining text");
                for (int i = 0; i < 3; i++) { session.Update(20); session.Advance(); }
                Check(session.Speaker == null && !p.Talking, "dialogue closes after three pages and resumes resident");
                session.Start(p); session.Close(); Check(!p.Talking && session.Speaker == null, "escape cancellation releases speaker");
            }
        }
        Check(rotations.Count == 4, "placement covers all four world rotations");
        Check(poses.Count == 3, "sitting, standing and wandering residents");
        Check(backrests > 0, "generated residents use existing walls or pillars as backrests");
        Check(populations.Count > 1, "island seeds vary population");
        var categories = new HashSet<ResidentAreaKind>();
        var sites = new HashSet<Vector3>();
        int sampled = 0;
        for (int seed = 1; seed <= 64; seed++)
        {
            var cell = seed > 32 ? (-54, -16) : seed % 2 == 0 ? (8, -4) : (0, 0);
            using var island = Sector.Generate(seed, cell.Item1, cell.Item2);
            Check(!island.ResidentFloorAllowed(island.Transform(new(22, 0, 22))), "enclosed tower interior excluded even through an open door");
            if (island.ChaosTier > 0 && island.SkylineStyle is 1 or 3)
                Check(!island.ResidentFloorAllowed(island.Transform(new(-22, 0, 22))), "walled highrise interior excluded");
            foreach (var p in island.ProceduralPeople)
            {
                sampled++; sites.Add(island.ResidentLocal(p.Anchor));
                var matches = island.ResidentAreas.Where(area => Enumerable.Range(0, 11).All(step =>
                    Residents.AllowedFootprint(island, area, Vector3.Lerp(p.Anchor, p.Patrol, step / 10f), p.Scale, p.Pose, p.Yaw))).ToArray();
                Check(matches.Length > 0, "random placement and complete patrol remain in allowed floor regions");
                foreach (var area in matches) categories.Add(area.Kind);
                Check(p.Pose == ResidentPose.Sitting ? Residents.SittingSupported(island.Collision, p.Anchor, p.Scale, p.Yaw)
                    : Residents.Supported(island.Collision, p.Anchor, p.Patrol, p.Scale * .48f), "random placements have supported feet, clear bodies and headroom");
                for (int frame = 0; frame < 1200; frame++)
                {
                    p.Update(.05f);
                    Check(p.Pose == ResidentPose.Sitting ? Residents.SittingSupported(island.Collision, p.Position, p.Scale, p.Yaw)
                        : Residents.Supported(island.Collision, p.Position, p.Position, p.Scale * .48f),
                        "60-second audit: feet remain supported and bodies clear of walls, including sky bridges");
                }
            }
        }
        Check(categories.Count == 4, "seeded placements cover island ground, open-building first and second floors, and bridges");
        Check(sites.Count > 40 && sites.Any(p => Math.Abs(p.X - MathF.Round(p.X)) > .1f), "continuous seeded positions replace the nine fixed anchors");
        Console.WriteLine($"RESIDENT PLACEMENT: {sampled} residents across 64 islands, {sites.Count} distinct positions and all {categories.Count} floor categories passed (60s each).");
        var mesh = new Geometry(); mesh.Box(new(0, -.5f, 0), new(2, 1, 2), Color.Gray);
        var small = new WalkableScene(); mesh.AddCollisionTo(small);
        Check(!Residents.Supported(small, Vector3.Zero, Vector3.Zero, 1.1f), "partial footprint over void rejected");
        var lowBeam = new Geometry();
        lowBeam.Box(new(0, -.5f, 0), new(4, 1, 4), Color.Gray);
        lowBeam.Box(new(.24f, .6f, .24f), new(.02f, 1.2f, .02f), Color.Gray);
        var bodyObstruction = new WalkableScene(); lowBeam.AddCollisionTo(bodyObstruction);
        Check(!Residents.Supported(bodyObstruction, Vector3.Zero, Vector3.Zero, .48f),
            "thin low obstruction between capsule samples is rejected by full body bounds");
        mesh = new Geometry(); mesh.Box(new(0, 1, 0), new(4, 2, .2f), Color.Gray);
        var wall = new WalkableScene(); mesh.AddCollisionTo(wall);
        Check(wall.Obstructs(new(0, 1, -2), new(0, 1, 2)) && wall.Obstructs(new(0, 1, 2), new(0, 1, -2)), "dialogue line of sight blocks both sides of walls");
        Check(!wall.Obstructs(new(3, 1, -2), new(3, 1, 2)), "open line of sight preserved");
        foreach (float yaw in new[] { 0f, MathF.PI / 2, MathF.PI, -MathF.PI / 2 })
            foreach (float scale in new[] { .68f, .96f, 1.04f })
            {
                var support = new Geometry();
                support.Box(new(0, -.5f, 0), new(16, 1, 16), Color.Gray);
                Vector3 normal = new(MathF.Sin(yaw), 0, MathF.Cos(yaw));
                support.Box(normal * 2 + Vector3.UnitY * 1.5f, new(5, 3, .4f), Color.Gray, yaw);
                var scene = new WalkableScene(); support.AddCollisionTo(scene);
                Check(Residents.TrySeatByWall(scene, Vector3.Zero, scale, out var seat, out float facing), "wall seat found for every scale and rotation");
                Vector3 forwardSeat = new(MathF.Sin(facing), 0, MathF.Cos(facing));
                Check(Vector3.Dot(forwardSeat, normal) < -.99f, "seated resident faces away from backrest");
                Check(Residents.SittingSupported(scene, seat, scale, facing), "wall seat leaves robe, head and body clear and supported");
                Check(scene.Obstructs(seat + Vector3.UnitY * .65f * scale,
                    seat + Vector3.UnitY * .65f * scale - forwardSeat * .34f * scale), "back stays close to wall");
            }
        Check(!Residents.TrySeatByWall(small, Vector3.Zero, 1, out _, out _), "no backrest invented on open floor");
        Check(!Residents.SittingSupported(small, new(0, 0, .5f), 1, 0), "extended seated hem cannot hang beyond floor edge");
        var pillarMesh = new Geometry();
        pillarMesh.Box(new(0, -.5f, 0), new(12, 1, 12), Color.Gray);
        pillarMesh.Cylinder(new(0, 0, 2), .6f, .6f, 3, Color.Gray, 16);
        var pillarScene = new WalkableScene(); pillarMesh.AddCollisionTo(pillarScene);
        Check(Residents.TrySeatByWall(pillarScene, Vector3.Zero, 1, out var pillarSeat, out float pillarYaw)
            && Residents.SittingSupported(pillarScene, pillarSeat, 1, pillarYaw), "round pillar supports a seated resident without clipping");
        using var world = new World(upload: false);
        var neighbors = world.Sectors.SelectMany(s => s.ProceduralPeople).ToArray();
        for (int i = 0; i < neighbors.Length; i++)
            for (int j = i + 1; j < neighbors.Length; j++)
                Check(Vector3.Distance(neighbors[i].Anchor, neighbors[j].Anchor)
                    - Vector3.Distance(neighbors[i].Anchor, neighbors[i].Patrol)
                    - Vector3.Distance(neighbors[j].Anchor, neighbors[j].Patrol) >= 15,
                    "residents and patrols stay separated across neighboring islands and bridge seams");
        var person = world.People.First(p => World.Cell(p.Anchor) == (0, 0));
        Vector3 eye = person.LookPoint + new Vector3(0, .8f, 2.4f), forward = Vector3.Normalize(person.LookPoint - eye);
        Check(Conversation.Target(world, eye, forward) == person, "nearby person can be selected");
        Check(Conversation.Target(world, eye, -forward) != person, "person behind player is not selected");
        Check(Conversation.Target(world, eye + Vector3.UnitY * 10, forward) != person, "cannot talk to this person from a distant roof");
        int oldCount = world.People.Count();
        world.EnsureAround(new(3840, 0, -3840), 0);
        Check(!world.People.Contains(person) && world.People.Count() <= 5, "eviction removes resident ownership");
        world.EnsureAround(world.Views[0].Position, 0);
        Check(world.People.Count() == origin.People.Count && oldCount <= 25 * 5 + SpecialContent.Data.Objects.Count(d => d.Kind == SpecialKind.Resident), "return restores seeded first-island population");
        Console.WriteLine($"PEOPLE: {count} supported residents, {backrests} backrests, all ages/poses/rotations, 60s patrols, dialogue lifecycle, range/occlusion and streaming checks passed.");
    }

    private static void CollisionChecks()
    {
        using var world = new World(upload: false);
        foreach (var s in world.Sectors) s.People.Clear();
        var sector = world.Sectors.First(s => s.X == 0 && s.Z == 0);
        Vector3 foot = new(0, 400, 0);
        var bridge = new Geometry();
        bridge.Box(foot - Vector3.UnitY * .35f, new(3, .7f, 30), Color.Gray);
        foreach (float side in new[] { -1.4f, 1.4f })
            bridge.Box(foot + new Vector3(side, .5f, 0), new(.2f, 1, 30), Color.Gray);
        bridge.AddCollisionTo(sector.Collision);
        Resident Person(ResidentPose pose, float scale) => new() {
            Id = 1, Profile = 0, Anchor = foot, Patrol = foot + Vector3.UnitZ * 2.5f,
            Pose = pose, Age = ResidentAge.Adult, Female = false, Scale = scale, Phase = 0
        };
        foreach (var pose in Enum.GetValues<ResidentPose>())
            foreach (float scale in new[] { .68f, .96f, 1.04f })
            {
                sector.People.Clear();
                var person = Person(pose, scale); person.Initialize(0); sector.People.Add(person);
                foreach (bool fast in new[] { false, true })
                    foreach (float lane in new[] { 0f, -.85f, .85f })
                    {
                        var player = new Explorer { Position = foot + new Vector3(lane, Explorer.EyeHeight, -3) };
                        for (int i = 0; i < (fast ? 12 : 30); i++)
                            player.Advance(.05f, world, Vector3.UnitZ, false, false, false, fast);
                        Check(lane == 0 ? player.Position.Z < -.4f : player.Position.Z > 3,
                            "small resident collider stops direct walking/Shift travel but leaves both bridge sides open");
                        Check(!world.Blocked(player.Position), "resident contact never embeds player in body or rails");
                    }
                var flyer = new Explorer { Flying = true, Position = foot + new Vector3(0, Explorer.EyeHeight, -3) };
                for (int i = 0; i < 10 && flyer.Flying; i++)
                    flyer.Advance(.05f, world, Vector3.UnitZ, false, false, false, true);
                Check(!flyer.Flying && flyer.FlightBumped && !world.Blocked(flyer.Position), "fast flight cannot pass through residents");
                var falling = new Explorer { Position = foot + new Vector3(0, Explorer.EyeHeight + 4, 0) };
                for (int i = 0; i < 60; i++) falling.Advance(.05f, world, Vector3.Zero, false, false, false, false);
                Check(Math.Abs(falling.Position.Y - Explorer.EyeHeight - person.CollisionTop) < .01f
                    && !world.Blocked(falling.Position), "falling player lands above resident instead of passing through");
                Check(!person.Blocks(foot + new Vector3(0, -2, 0))
                    && !person.Blocks(foot + new Vector3(0, Explorer.EyeHeight + 3, 0)), "other elevations stay clear");
            }
        sector.People.Clear();
        var walker = Person(ResidentPose.Walking, 1); walker.Initialize(0); sector.People.Add(walker);
        Vector3 waiting = foot + new Vector3(0, Explorer.EyeHeight, 1);
        for (int i = 0; i < 100; i++) { walker.Update(.05f, waiting); Check(!walker.Blocks(waiting), "walking resident waits before entering player"); }
        Vector3 stopped = walker.Position;
        walker.Update(.05f, waiting + Vector3.UnitX * 2);
        Check(walker.Position != stopped, "resident resumes after player moves aside");
        walker.Shift(new(-192, 0, 0));
        Check(walker.Blocks(walker.Position + Vector3.UnitY * Explorer.EyeHeight), "collider follows moving resident and floating origin");
        Console.WriteLine("RESIDENT COLLISION: all poses/scales, both sides of a three-unit bridge, walking/Shift/flight/falls and moving resident contact passed.");
    }
}
