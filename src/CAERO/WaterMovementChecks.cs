using System.Numerics;

namespace Caero;

internal static class WaterMovementChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Water movement test failed: " + message); }
    public static void Run(World world)
    {
        float shallow = Drop(world, .5f, .02f);
        float medium = Drop(world, 8, .02f);
        float deep = Drop(world, 80, .02f);
        Check(medium > shallow + 1 && deep > medium + 4 && deep > 10,
            $"faster impacts sink deeper: {shallow:F2}, {medium:F2}, {deep:F2}");
        foreach (float dt in new[] { .01f, .05f, .1f })
            Check(Math.Abs(Drop(world, 80, dt) - deep) < .3f, "entry depth remains stable across frame times");

        var swimmer = new Explorer { Position = new(90, World.WaterLevel - 10, 90) };
        float start = swimmer.Position.Y;
        Advance(swimmer, world, .2f);
        Check(swimmer.Position.Y > start && swimmer.Position.Y < start + .15f, "buoyancy begins with a gentle rise");
        Advance(swimmer, world, 3);
        Check(swimmer.Position.Y > start + 3, "passive buoyancy continues returning the swimmer");
        Advance(swimmer, world, 3, descend: true);
        float dived = swimmer.Position.Y;
        Check(dived < start, "C dives against buoyancy");
        Advance(swimmer, world, 3, rise: true);
        Check(swimmer.Position.Y > dived + 5, "Space still swims upward");

        swimmer = new Explorer { Position = new(90, World.SeaFloor + Explorer.EyeHeight, 90) };
        Advance(swimmer, world, 2, descend: true);
        Check(swimmer.Position.Y >= World.SeaFloor + Explorer.EyeHeight - .02f, "diving cannot penetrate seabed");
        Advance(swimmer, world, 2);
        Check(swimmer.Position.Y > World.SeaFloor + Explorer.EyeHeight + 1, "buoyancy lifts swimmer away from seabed");
        Console.WriteLine($"WATER: sink depths {shallow:F2}, {medium:F2}, {deep:F2} from falls of 0.5, 8, 80; buoyancy and swimming passed.");
    }
    private static float Drop(World world, float height, float dt)
    {
        var explorer = new Explorer { Position = new(90, World.WaterLevel + height + Explorer.EyeHeight, 90) };
        float minimum = explorer.Position.Y;
        bool entered = false, checkedEntry = false;
        for (int i = 0; i < (int)(30 / dt); i++)
        {
            bool wasSwimming = explorer.Swimming;
            float oldY = explorer.Position.Y;
            explorer.Advance(dt, world, Vector3.Zero, false, false, false, false);
            minimum = Math.Min(minimum, explorer.Position.Y);
            if (wasSwimming && entered && !checkedEntry)
            {
                if (height >= 80) Check((explorer.Position.Y - oldY) / dt < -20, "water entry preserves fast downward motion");
                checkedEntry = true;
            }
            entered |= explorer.Swimming;
            Check(explorer.Position.Y >= World.SeaFloor + Explorer.EyeHeight - .02f, "fall stops at seabed");
        }
        Check(entered && checkedEntry, "fall enters swimming state");
        Check(Math.Abs(explorer.Position.Y - (World.WaterLevel + .6f)) < .1f, "idle swimmer eventually returns to surface");
        return World.WaterLevel - minimum;
    }
    private static void Advance(Explorer explorer, World world, float seconds, bool rise = false, bool descend = false)
    {
        for (int i = 0; i < (int)(seconds / .02f); i++)
            explorer.Advance(.02f, world, Vector3.Zero, rise, false, descend, false);
    }
}
