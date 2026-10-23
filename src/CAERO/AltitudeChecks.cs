using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class AltitudeChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Altitude check failed: " + message); }

    internal static void Run()
    {
        var boundary = new AltitudeBoundary();
        Check(!boundary.Update(499.9f, 10) && !boundary.Update(500, 10) && !boundary.Active && boundary.Strength == 0,
            "normal altitude and the exact threshold do not trigger distortion");
        Check(!boundary.Update(500.01f, 0) && boundary.Active && boundary.Strength is > 0 and < .1f,
            "crossing the threshold starts a faint distortion");
        boundary.Update(501, 1);
        float faint = boundary.Strength;
        Check(!boundary.Update(499, 1) && boundary.Strength > faint, "distortion grows and cannot be cancelled by descending");
        var camera = new Camera3D { Position = new(0, 501, 0), Target = new(0, 501, -1), Up = Vector3.UnitY, FovY = 62 };
        var distorted = boundary.Distort(camera);
        Check(distorted.Position == camera.Position && distorted.Target != camera.Target && camera.Target == new Vector3(0, 501, -1),
            "view shakes without moving or rotating the real player");
        Check(!boundary.Update(501, .99f) && boundary.Update(501, .02f), "return occurs after the full distortion duration");
        boundary.Reset();
        Check(!boundary.Active && boundary.Elapsed == 0 && boundary.Strength == 0 && boundary.Distort(camera).Target == camera.Target,
            "return completely clears the visual effect");
        Console.WriteLine("ALTITUDE: threshold, escalating distortion, latched return, render-only shake and reset passed.");
    }

    internal static void Capture()
    {
        Run();
        string savePath = Path.Combine(AppContext.BaseDirectory, "altitude-check-" + Guid.NewGuid() + ".json");
        WindowHost.Open();
        try
        {
            using var text = new ConversationUi();
            var inventory = SpecialContent.Data.Objects.Where(d => d.Kind == SpecialKind.Item).Select(d => d.Id).Order().ToArray();
            var start = new SavedView(0, 500, 0, 0, -.8f, true,
                GeneratorVersion: SaveStore.CurrentGeneratorVersion, IslandX: 20, IslandZ: 0, CoordinateVersion: 1,
                Progress: new(inventory));
            string folder = Path.Combine(SaveStore.DirectoryPath, "Captures"); Directory.CreateDirectory(folder);
            using (var game = new GameSession(World.DefaultSeed, start, false, savePath))
            {
                game.Player.SetCaptured(false);
                void Shot(string name)
                {
                    for (int i = 0; i < 3; i++)
                    { Raylib.BeginDrawing(); game.Draw(text, true); Raylib.EndDrawing(); }
                    var image = Raylib.LoadImageFromScreen();
                    try { Check(Raylib.ExportImage(image, Path.Combine(folder, "altitude-" + name + ".png")), "native capture"); }
                    finally { Raylib.UnloadImage(image); }
                }
                game.Update(.01f);
                Check(!game.Boundary.Active && !game.Respawning, "exactly 500 remains playable in the live session");
                Shot("normal");
                game.Player.Position.Y = 500.1f;
                var pose = game.Player.Position; float yaw = game.Player.Yaw, pitch = game.Player.Pitch;
                game.Conversation.Confirm("Test", "Question?", () => throw new InvalidOperationException("unexpected choice"));
                game.Update(.01f);
                Check(game.Boundary.Active && game.Respawning && !game.Conversation.Active, "crossing closes dialogue and locks input");
                foreach (var (name, seconds) in new[] { ("faint", .3f), ("medium", 1.5f), ("strong", 2.9f) })
                {
                    game.Update(seconds - game.Boundary.Elapsed); Shot(name);
                    Check(game.Player.Position == pose && game.Player.Yaw == yaw && game.Player.Pitch == pitch,
                        "distortion never changes the real pose");
                }
                game.Update(AltitudeBoundary.GlitchDuration - game.Boundary.Elapsed + .01f);
                Check(!game.Boundary.Active && game.Respawning && game.World.OriginX == 0 && game.World.OriginZ == 0
                    && game.Player.Position == game.World.Views[0].Position && !game.Player.Flying && !game.World.Blocked(game.Player.Position),
                    "distortion returns to the first island on solid ground and ends flight");
                Shot("black");
                game.Update(1.1f); Shot("message");
                game.Update(3.5f); Shot("fade-in");
                game.Update(1);
                Check(!game.Respawning && !game.Boundary.Active && !game.World.Progress.Escaped
                    && game.World.Progress.Snapshot().Collected.Order().SequenceEqual(inventory),
                    "normal play resumes with unchanged inventory and no ending trigger");
                Shot("returned");
                var saved = SaveStore.Load(savePath)!;
                Check(saved.IslandX == 0 && saved.IslandZ == 0 && saved.Y < AltitudeBoundary.HeightLimit && !saved.Flying
                    && saved.Progress!.Collected.Order().SequenceEqual(inventory), "immediate save records the safe return and preserves all items");
            }
            using var resumed = new GameSession(World.DefaultSeed, SaveStore.Load(savePath), false, savePath);
            Check(!resumed.Boundary.Active && resumed.Player.Position.Y < AltitudeBoundary.HeightLimit,
                "Continue resumes safely without repeating the boundary effect");
            resumed.Player.SetCaptured(false);
            Console.WriteLine("ALTITUDE RENDER: all glitch stages, frozen pose, respawn sequence, safe autosave, inventory and Continue passed; " + folder);
        }
        finally
        {
            File.Delete(savePath); if (File.Exists(savePath + ".tmp")) File.Delete(savePath + ".tmp");
            Raylib.CloseWindow();
        }
    }
}
