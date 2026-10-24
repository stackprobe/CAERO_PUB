using Raylib_cs;

namespace Caero;

internal static class StartupChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Startup check failed: " + message); }
    internal static void Run()
    {
        (double Seconds, float Logo, float Title, bool Active)[] cues =
        [
            (0, 0, 0, true), (.49, 0, 0, true), (.5, 0, 0, true), (.75, .5f, 0, true),
            (1, 1, 0, true), (2.99, 1, 0, true), (3, 1, 0, true), (3.25, .5f, 0, true),
            (3.5, 0, 0, true), (3.75, 0, .5f, true), (4, 0, 1, false), (20, 0, 1, false)
        ];
        foreach (var cue in cues)
        {
            var sequence = new StartupSequence(); sequence.Update(cue.Seconds);
            Check(Math.Abs(sequence.LogoOpacity - cue.Logo) < .0001f
                && Math.Abs(sequence.TitleOpacity - cue.Title) < .0001f && sequence.Active == cue.Active,
                $"logo/title opacity and menu lock at {cue.Seconds}s");
        }
        var stepped = new StartupSequence();
        foreach (double dt in new[] { .5, .5, 2, .5, .5 }) stepped.Update(dt);
        Check(!stepped.Active && stepped.TitleOpacity == 1, "frame steps finish at four seconds");
        stepped.Update(-10);
        Check(!stepped.Active, "clock cannot run backwards or replay the credit after returning to title");
        Console.WriteLine("STARTUP: black, official logo fade/hold/fade, title fade and menu unlock at 4s passed.");
    }
    internal static void Capture()
    {
        Run(); WindowHost.Open();
        try
        {
            using var credit = new StartupCredit(); using var text = new ConversationUi(); using var menus = new GameMenus(text);
            var flow = new GameFlow(); flow.ToTitle(true);
            string folder = Path.Combine(SaveStore.DirectoryPath, "Captures", "Startup"); Directory.CreateDirectory(folder);
            foreach (var (width, height) in new[] { (1440, 900), (640, 360) })
            {
                Raylib.SetWindowSize(width, height);
                foreach (double seconds in new[] { 0, .75, 1, 3.25, 3.5, 3.75, 4 })
                {
                    var sequence = new StartupSequence(); sequence.Update(seconds);
                    for (int frame = 0; frame < 3; frame++)
                    { Raylib.BeginDrawing(); credit.Draw(sequence, menus, flow, true); Raylib.EndDrawing(); }
                    var image = Raylib.LoadImageFromScreen();
                    try
                    {
                        Check(image.Width == width && image.Height == height, "capture dimensions");
                        if (seconds is 0 or 3.5)
                        {
                            Color center = Raylib.GetImageColor(image, width / 2, height / 2);
                            Check(center.R == 0 && center.G == 0 && center.B == 0, "black between stages");
                        }
                        Check(Raylib.ExportImage(image, Path.Combine(folder, $"startup-{width}-{seconds:0.00}.png")), "capture export");
                    }
                    finally { Raylib.UnloadImage(image); }
                }
            }
            Console.WriteLine("STARTUP RENDER: official logo, hidden menus during title fade and finished menu at 1440x900 / 640x360; " + folder);
        }
        finally { Raylib.CloseWindow(); }
    }
}
