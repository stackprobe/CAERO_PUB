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
        using var origin = Sector.Generate(1701, 0, 0);
        Check(origin.People.Count == 3, "three scattered residents present on the first island");
        Check(origin.People.Select(p => p.Age).Distinct().Count() == 3 && origin.People.Any(p => p.Female) && origin.People.Any(p => !p.Female), "children, adults, elders, women and men");
        Check(origin.People.Select(p => p.Pose).Distinct().Count() == 3, "walking, crouching and collapsed poses");
        var rotations = new HashSet<int>(); int count = 0;
        foreach (var (seed, x, z) in new[] { (1701, 0, 0), (1701, 1, 0), (1701, -3, -4), (1701, 15, 10), (1, 8, -4), (2, 8, -4), (3, 8, -4), (4, 8, -4) })
        {
            using var a = Sector.Generate(seed, x, z); using var b = Sector.Generate(seed, x, z);
            rotations.Add(a.Rotation);
            Check(a.People.Select(p => (p.Id, p.Profile, p.Anchor, p.Patrol, p.Scale, p.Yaw)).SequenceEqual(b.People.Select(p => (p.Id, p.Profile, p.Anchor, p.Patrol, p.Scale, p.Yaw))), "stable regeneration without altering map random sequence");
            Check(a.People.Count <= (x == 0 && z == 0 ? 3 : 2), "sparse bounded population per sector");
            foreach (var p in a.People)
                foreach (var q in a.People.Where(q => q.Id != p.Id))
                    Check(Vector3.Distance(p.Anchor, q.Anchor) - Vector3.Distance(p.Anchor, p.Patrol)
                        - Vector3.Distance(q.Anchor, q.Patrol) >= 15, "patrols stay at least fifteen units apart");
            foreach (var p in a.People)
            {
                var speech = Dialogue.Lines[p.Profile];
                Check(p.Age == speech.Age && p.Pose == speech.Pose && p.Female == (speech.Gender == "female"),
                    "external dialogue metadata matches the rendered speaker");
                count++;
                float radius = p.Scale * (p.Pose == ResidentPose.Collapsed ? 1.05f : .48f);
                Check(Residents.Supported(a.Collision, p.Anchor, p.Patrol, radius), "whole body and patrol have a clear supported footprint");
                for (int i = 0; i < 1200; i++)
                {
                    p.Update(.05f);
                    Check(Vector3.Distance(p.Position, p.Anchor) <= 2.501f && float.IsFinite(p.Yaw), "slow walking stays inside validated path");
                    Check(Math.Abs(a.Collision.FloorAt(p.Position.X, p.Position.Z, p.Position.Y + .05f) - p.Position.Y) < .04f, "no floating or falling through floors");
                }
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
        var mesh = new Geometry(); mesh.Box(new(0, -.5f, 0), new(2, 1, 2), Color.Gray);
        var small = new WalkableScene(); mesh.AddCollisionTo(small);
        Check(!Residents.Supported(small, Vector3.Zero, Vector3.Zero, 1.1f), "partial footprint over void rejected");
        mesh = new Geometry(); mesh.Box(new(0, 1, 0), new(4, 2, .2f), Color.Gray);
        var wall = new WalkableScene(); mesh.AddCollisionTo(wall);
        Check(wall.Obstructs(new(0, 1, -2), new(0, 1, 2)) && wall.Obstructs(new(0, 1, 2), new(0, 1, -2)), "dialogue line of sight blocks both sides of walls");
        Check(!wall.Obstructs(new(3, 1, -2), new(3, 1, 2)), "open line of sight preserved");
        using var world = new World(upload: false);
        var person = world.People.First(p => p.Profile == 0 && World.Cell(p.Anchor) == (0, 0));
        Vector3 eye = person.LookPoint + new Vector3(0, .8f, 2.4f), forward = Vector3.Normalize(person.LookPoint - eye);
        Check(Conversation.Target(world, eye, forward) == person, "nearby person can be selected");
        Check(Conversation.Target(world, eye, -forward) == null, "person behind player is not selected");
        Check(Conversation.Target(world, eye + Vector3.UnitY * 10, forward) == null, "cannot talk from distant roof");
        int oldCount = world.People.Count();
        world.EnsureAround(new(3840, 0, -3840), 0);
        Check(!world.People.Contains(person) && world.People.Count() <= 2, "eviction removes resident ownership");
        world.EnsureAround(world.Views[0].Position, 0);
        Check(world.People.Count() == 3 && oldCount <= 3 + 24 * 2, "return restores bounded first-island population");
        Console.WriteLine($"PEOPLE: {count} supported residents, all ages/poses/rotations, 60s patrols, dialogue lifecycle, range/occlusion and streaming checks passed.");
    }
}
