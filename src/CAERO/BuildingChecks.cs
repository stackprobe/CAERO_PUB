using System.Numerics;

namespace Caero;

internal static class BuildingChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Building test failed: " + message); }

    internal static void Run()
    {
        using var world = new World(upload: false);
        var counts = new HashSet<int>();
        var rotations = new HashSet<int>();
        int facades = 0, walks = 0, jumps = 0;
        var cells = world.Sectors.Select(s => (s.X, s.Z)).ToArray();
        foreach (var (x, z) in cells)
        {
            world.Teleport(new Explorer(), new(x, z, new(0, Explorer.EyeHeight, 0)), radius: 0);
            var s = world.Sectors.First(s => s.X == x && s.Z == z);
            using var repeat = Sector.Generate(world.Seed, x, z);
            using var again = Sector.Generate(world.Seed, x, z);
            Check(s.TowerEntranceMask == repeat.TowerEntranceMask && again.Stone.Vertices.SequenceEqual(repeat.Stone.Vertices),
                $"deterministic entrance layout and geometry at {x},{z}");
            int count = 0;
            Vector3[] doors = [new(22, 0, 33.5f), new(22, 0, 10.5f), new(12.5f, 0, 12.5f), new(31.5f, 0, 12.5f)];
            Vector3[] normals = [Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitX];
            for (int side = 0; side < 4; side++)
            {
                bool open = (s.TowerEntranceMask & (1 << side)) != 0;
                Check(s.Collision.Blocked(s.Transform(doors[side] + normals[side] * .5f + Vector3.UnitY * Explorer.EyeHeight)) != open,
                    $"facade {side} collision matches entrance at {x},{z}");
                if (open) { count++; facades |= 1 << side; }
            }
            counts.Add(count); rotations.Add(s.Rotation);
            var routes = s.Routes.Where(r => r.Name.StartsWith("tower "))
                .Select(r => (r.Name, Points: r.Points.Select(world.AddressOf).ToArray())).ToArray();
            foreach (var route in routes)
            {
                ProceduralChecks.Walk(world, route.Name, route.Points);
                ProceduralChecks.Walk(world, route.Name + " reverse", route.Points.Reverse().ToArray());
                walks += 2;
            }
            // Exercise real jump/collision physics, including the largest frame step.
            foreach (float dt in new[] { .01f, .05f, .1f })
                foreach (float cx in new[] { -35f, 35f })
                    foreach (float cz in new[] { -35f, 35f })
                    {
                        var e = new Explorer { Position = s.Transform(new(cx, Explorer.EyeHeight, cz - MathF.Sign(cz) * 5.5f)) };
                        JumpTo(new(cx, 1, cz - MathF.Sign(cz) * 3.5f));
                        JumpTo(new(cx, 2, cz));
                        void JumpTo(Vector3 localTarget)
                        {
                            Vector3 target = s.Transform(localTarget);
                            bool first = true;
                            for (int frame = 0; frame < (int)(1.5f / dt); frame++)
                            {
                                Vector3 delta = target - e.Position; delta.Y = 0;
                                float distance = delta.Length();
                                Vector3 move = distance > .02f ? Vector3.Normalize(delta) * Math.Min(1, distance / (5 * dt)) : Vector3.Zero;
                                e.Advance(dt, world, move, first, first, false, false);
                                first = false;
                            }
                            Check(Math.Abs(e.Position.Y - Explorer.EyeHeight - target.Y) < .03f && !world.Blocked(e.Position),
                                $"corner jump dt={dt}, island={x},{z}, target={localTarget}, actual={e.Position}");
                            Check(Vector2.Distance(new(e.Position.X, e.Position.Z), new(target.X, target.Z)) < .05f, "jump reaches block center");
                            jumps++;
                        }
                    }
        }
        Check(counts.SetEquals([1, 2, 3, 4]) && facades == 15 && rotations.Count == 4,
            "sample covers all entrance counts, facades and island rotations");
        Console.WriteLine($"BUILDINGS: seeded 1-4 entrances, four facades/rotations, regeneration, {walks} entrance/stair walks and {jumps} corner jumps passed.");
    }
}
