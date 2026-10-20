using System.Numerics;

namespace Caero;

internal static class DayNightChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Day/night test failed: " + message); }
    public static void Run()
    {
        var clock = new DayNightCycle();
        clock.Advance(299.9); Check(!clock.State.IsNight, "day lasts five minutes");
        clock.Advance(.1); Check(clock.State.IsNight, "night starts at 300 seconds");
        clock.Advance(300); Check(!clock.State.IsNight, "day returns at 600 seconds");
        Check(clock.TimeScale == 1, "clock starts at normal speed");
        clock.ToggleSpeed();
        Check(clock.TimeScale == 10 && clock.Seconds == 600, "N enables 10x without changing the current time");
        clock.Advance(30);
        Check(clock.Seconds == 900 && clock.State.IsNight, "10x advances a half-cycle in 30 real seconds");
        clock.ToggleSpeed(); clock.Advance(30);
        Check(clock.TimeScale == 1 && clock.Seconds == 930, "N restores normal speed without leftover advance");
        clock.ToggleSpeed(); clock.Advance(1);
        Check(clock.TimeScale == 10 && clock.Seconds == 940, "speed toggle repeats");
        var noon = DayNightCycle.At(150); var midnight = DayNightCycle.At(450);
        Check(noon.Night == 0 && midnight.Night == 1, "full daylight and night brightness");
        Check(noon.Sun.Y > .79f && midnight.Moon.Y > .79f, "sun and moon rise overhead");
        for (double t = 0; t <= 1200; t += .25)
        {
            var a = DayNightCycle.At(t); var b = DayNightCycle.At(t + .05);
            Check(Vector3.Distance(a.Sun, -a.Moon) < .00001f && Math.Abs(a.Sun.Length() - 1) < .00001f, "opposed normalized celestial directions");
            Check(Vector3.Distance(a.Sun, b.Sun) < .001f && Math.Abs(a.Night - b.Night) < .003f, "motion and ambient light are continuous");
            Check(a.Sun.Y > 0 || a.SunOpacity == 0, "sun disappears below horizon");
            Check(a.Moon.Y > 0 || a.MoonOpacity == 0, "moon disappears below horizon");
        }
        Check(DayNightCycle.At(298).SunOpacity < DayNightCycle.At(294).SunOpacity, "setting sun fades out");
        Check(DayNightCycle.At(304).MoonOpacity < DayNightCycle.At(310).MoonOpacity, "rising moon fades in");
        Check(DayNightCycle.At(598).MoonOpacity < DayNightCycle.At(594).MoonOpacity, "setting moon fades out");
        Check(DayNightCycle.At(604).SunOpacity < DayNightCycle.At(610).SunOpacity, "rising sun fades in");

        var schedule = new LanternSchedule(12345);
        var positions = Enumerable.Range(0, 24).Select(i => new Vector3(i * 3, 1.9f, 3)).ToArray();
        var dusk = positions.Select(p => schedule.Brightness(p, 300)).ToArray();
        var dawn = positions.Select(p => schedule.Brightness(p, 600)).ToArray();
        Check(dusk.Distinct().Count() > 2 && dusk.Any(x => x == 0) && dusk.Any(x => x > .5f), "lamps switch on at staggered times");
        Check(dawn.Distinct().Count() > 2 && dawn.Any(x => x == 1) && dawn.Any(x => x < .5f), "lamps switch off at staggered times");
        Check(positions.Any(p => Math.Abs(schedule.Brightness(p, 300) - schedule.Brightness(p, 900)) > .1f), "each night has new timings");
        foreach (var p in positions)
        {
            float atDusk = schedule.Brightness(p, 300);
            Check(schedule.Brightness(p, 150) == 0 && schedule.Brightness(p, 450) == 1, "daytime off and nighttime on");
            float previous = 0;
            for (double t = 280; t <= 340; t += .05)
            {
                float brightness = schedule.Brightness(p, t);
                Check(brightness >= previous - .00001f && brightness - previous < .027f, "lamp fade-in is monotone and gradual"); previous = brightness;
            }
            previous = 1;
            for (double t = 580; t <= 640; t += .05)
            {
                float brightness = schedule.Brightness(p, t);
                Check(brightness <= previous + .00001f && previous - brightness < .027f, "lamp fade-out is monotone and gradual"); previous = brightness;
            }
            Check(previous == 0 && new LanternSchedule(12345).Brightness(p, 300) == atDusk, "revisited lamps retain current fade state");
        }
        Console.WriteLine("SKY: 300-second day/night phases, horizon fades, continuous light, staggered randomized lamp fades passed.");
    }
}
