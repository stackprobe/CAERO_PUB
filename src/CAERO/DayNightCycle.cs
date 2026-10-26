using System.Numerics;

namespace Caero;

internal readonly record struct SkyState(double Seconds, bool IsNight, float Night,
    Vector3 Sun, Vector3 Moon, float SunOpacity, float MoonOpacity);

internal sealed class DayNightCycle
{
    public const double HalfPeriod = 300, Period = HalfPeriod * 2;
    public double Seconds { get; private set; }
    public int TimeScale { get; private set; } = 1;
    public SkyState State => At(Seconds);
    public void Advance(double dt)
    {
        if (!double.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        Seconds += dt * TimeScale;
    }
    public void ToggleSpeed() => TimeScale = TimeScale == 1 ? 10 : 1;
    public void Restore(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        // The lighting blend reaches its endpoints at sun.Y = +/- .15.
        // Include five seconds on either side of twilight and snap away from
        // its midpoint. The midpoint itself resumes on the later side.
        double margin = Math.Asin(.15 / .8) * Period / Math.Tau + 5;
        double phase = seconds % HalfPeriod;
        Seconds = seconds - phase + Math.Clamp(phase, margin, HalfPeriod - margin);
        TimeScale = 1;
    }
    public void StartAtNight() { Seconds = HalfPeriod * 1.5; TimeScale = 1; }
    public static SkyState At(double seconds)
    {
        double phase = ((seconds % Period) + Period) % Period;
        float angle = (float)(phase / Period * Math.Tau);
        float sine = MathF.Sin(angle);
        Vector3 sun = new(MathF.Cos(angle), sine * .8f, sine * .6f);
        Vector3 moon = -sun;
        float night = 1 - Smooth((sun.Y + .15f) / .30f);
        return new(seconds, phase >= HalfPeriod, night, sun, moon, Smooth(sun.Y / .06f), Smooth(moon.Y / .06f));
    }
    internal static float Smooth(float value)
    {
        float t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

// Revisited islands keep their schedule within a cycle; each night gets new timings.
internal sealed class LanternSchedule(int salt)
{
    public float Brightness(NightLight lamp, double seconds)
    {
        long cycle = (long)Math.Floor(seconds / DayNightCycle.Period);
        uint Hash(long night) => unchecked((uint)Sector.ValueAt(salt ^ (int)night ^ lamp.Index * 941, lamp.IslandX, lamp.IslandZ));
        return Math.Max(ForNight(Hash(cycle), seconds, cycle), ForNight(Hash(cycle - 1), seconds, cycle - 1));
    }
    public float Brightness(Vector3 position, double seconds)
    {
        long cycle = (long)Math.Floor(seconds / DayNightCycle.Period);
        return Math.Max(ForNight(position, seconds, cycle), ForNight(position, seconds, cycle - 1));
    }
    private float ForNight(Vector3 p, double time, long cycle)
    {
        unchecked
        {
            uint hash = (uint)Sector.ValueAt(salt ^ (int)cycle, BitConverter.SingleToInt32Bits(p.X), BitConverter.SingleToInt32Bits(p.Z));
            hash ^= (uint)BitConverter.SingleToInt32Bits(p.Y) * 0x9e3779b9u;
            return ForNight(hash, time, cycle);
        }
    }
    private static float ForNight(uint hash, double time, long cycle)
    {
        unchecked
        {
            float Random(int channel)
            {
                uint h = hash + (uint)channel * 0x85ebca6bu;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
            double nightStart = cycle * DayNightCycle.Period + DayNightCycle.HalfPeriod;
            double on = nightStart - 12 + Random(0) * 28;
            double off = nightStart + DayNightCycle.HalfPeriod - 12 + Random(1) * 28;
            float fadeIn = DayNightCycle.Smooth((float)((time - on) / (3 + Random(2) * 6)));
            float fadeOut = DayNightCycle.Smooth((float)((time - off) / (3 + Random(3) * 6)));
            return fadeIn * (1 - fadeOut);
        }
    }
}
