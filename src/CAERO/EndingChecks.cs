using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class EndingChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Ending check failed: " + message); }

    internal static void Run()
    {
        var ending = new EndingSequence();
        Check(ending.WhiteOpacity == 0 && ending.TextOpacity == 0 && ending.ImageOpacity == 0 && !ending.CanReturn,
            "ending begins on the frozen scene");
        ending.Update(1);
        Check(ending.WhiteOpacity is > 0 and < 1 && ending.TextOpacity == 0, "scene fades to white before the text");
        ending.Update(1.2f);
        Check(ending.WhiteOpacity == 1 && ending.TextOpacity == 0 && ending.ImageOpacity == 0, "brief pure white screen");
        ending.Update(1);
        Check(ending.TextOpacity is > 0 and < 1 && ending.ImageOpacity == 0, "message fades in over white");
        ending.Update(1);
        Check(ending.TextOpacity == 1 && ending.ImageOpacity == 0, "full escape message holds before the image");
        ending.Update(2.3f);
        Check(ending.TextOpacity == 0 && ending.ImageOpacity is > 0 and < 1 && !ending.CanReturn,
            "ending art gradually replaces white and the message");
        ending.Update(1.5f);
        Check(ending.ImageOpacity == 1 && !ending.CanReturn, "complete art remains visible before the button");
        for (int i = 0; i < 250; i++)
        {
            Check(!ending.ReturnInput(true, true), "rapid clicks cannot skip the image hold");
            ending.Update(.01f);
        }
        Check(!ending.CanReturn, "button stays hidden for three seconds after the full image");
        ending.Update(.6f);
        Check(ending.CanReturn && !ending.ReturnInput(true, true), "held confirmation cannot activate a newly visible button");
        Check(!ending.ReturnInput(false, false) && ending.ReturnInput(true, true), "fresh press after release returns to title");
        var flow = new GameFlow(); flow.Start(); flow.ToTitle();
        Check(flow.Screen == GameScreen.Title, "return leads to the title");
        Console.WriteLine("ENDING: white/text/image fades, image hold, delayed button, rapid-click and held-key protection passed.");
    }

    internal static void Capture()
    {
        string savePath = Path.Combine(AppContext.BaseDirectory, "ending-check-" + Guid.NewGuid() + ".json");
        WindowHost.Open();
        try
        {
            using var text = new ConversationUi();
            var exit = SpecialContent.Data.Objects.Single(d => d.Kind == SpecialKind.Exit);
            var (x, z) = SpecialChecks.Address(int.Parse(exit.Island));
            var inventory = SpecialContent.Data.Objects.Where(d => d.Kind == SpecialKind.Item).Select(d => d.Id).ToArray();
            var start = new SavedView(22, 6 + Explorer.EyeHeight, 14.39f, MathF.PI, -.22f, false,
                GeneratorVersion: SaveStore.CurrentGeneratorVersion, IslandX: x, IslandZ: z, CoordinateVersion: 1,
                Progress: new(inventory));
            string folder = Path.Combine(SaveStore.DirectoryPath, "Captures"); Directory.CreateDirectory(folder);
            void Shot(string label, GameSession game)
            {
                for (int frame = 0; frame < 3; frame++)
                { Raylib.BeginDrawing(); game.Draw(text, true); Raylib.EndDrawing(); }
                var image = Raylib.LoadImageFromScreen();
                try { Check(Raylib.ExportImage(image, Path.Combine(folder, "ending-" + label + ".png")), "native ending capture"); }
                finally { Raylib.UnloadImage(image); }
            }
            using (var game = new GameSession(1701, start, false, savePath))
            {
                var prop = game.World.Sectors.SelectMany(s => s.SpecialObjects).Single(o => o.Definition.Id == exit.Id);
                Vector3 eye = prop.Foot + prop.LocalFacing * 2.19f + Vector3.UnitY * Explorer.EyeHeight;
                game.Player.GoTo(new("EXIT CHECK", eye, prop.ExaminePoint)); game.Player.SetCaptured(false);
                SpecialInteraction.Examine(game.World, game.Player, prop, game.Conversation);
                Shot("question", game);
                game.Conversation.SelectAnswer(false); game.Conversation.Advance();
                Check(!game.World.Progress.Escaped, "No cancels in the live game session");
                SpecialInteraction.Examine(game.World, game.Player, prop, game.Conversation);
                game.Conversation.Advance(); game.Update(0);
                Check(game.World.Progress.Escaped && !game.Player.Captured, "Yes starts ending and releases the pointer");
                var saved = SaveStore.Load(savePath)!;
                Check(saved.Progress is { Escaped: false } && saved.Progress.Collected.Order().SequenceEqual(inventory.Order()),
                    "immediate goal autosave retains every acquired item and permits Continue");
                Check(Vector3.Distance(game.World.LocalOf(new(saved.IslandX, saved.IslandZ, new(saved.X, saved.Y, saved.Z))), eye) < .001f
                    && Math.Abs(saved.Yaw - game.Player.Yaw) < .001f && Math.Abs(saved.Pitch - game.Player.Pitch) < .001f,
                    "goal autosave records the supported approach and relic-facing view");
                var sky = game.Renderer.Sky;
                var animations = game.World.People.Select(p => p.Animation).ToArray();
                game.Update(.1f);
                Check(game.Renderer.Sky.Equals(sky) && game.World.People.Select(p => p.Animation).SequenceEqual(animations),
                    "ending freezes the simulation");
                foreach (var (label, seconds) in new[] { ("white-fade", 1f), ("white", 2.2f), ("message", 4.2f),
                    ("image-fade", 6.5f), ("image-wait", 8.5f), ("return-button", 11.5f) })
                {
                    game.Ending.Update(seconds - game.Ending.Elapsed); Shot(label, game);
                }
                Raylib.SetWindowSize(640, 360); Shot("return-button-small", game);
                Raylib.SetWindowSize(1440, 900);
            }
            using (var resumed = new GameSession(1, SaveStore.Load(savePath), false, savePath))
            {
                resumed.Player.SetCaptured(false);
                Check(!resumed.World.Progress.Escaped && resumed.World.Progress.Snapshot().Collected.Order().SequenceEqual(inventory.Order()),
                    "Continue preserves goal inventory and resumes normal play");
                Check(SpecialInteraction.Target(resumed.World, resumed.Player.Position, resumed.Player.Forward)?.Definition.Id == exit.Id,
                    "Continue faces the relic close enough to examine again");
                Shot("continue-before-relic", resumed);
            }
            Console.WriteLine("ENDING RENDER: confirmation, all fade stages, delayed button, frozen simulation, immediate autosave, Continue and small layout passed; " + folder);
        }
        finally
        {
            File.Delete(savePath); if (File.Exists(savePath + ".tmp")) File.Delete(savePath + ".tmp");
            Raylib.CloseWindow();
        }
    }
}
