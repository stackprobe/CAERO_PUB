using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class MenuChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Menu/save check failed: " + message); }
    internal static void Run()
    {
        StartupChecks.Run();
        CheckTransitions();
        ConversationInputChecks.Run();
        Check(!ConversationUi.SwallowedMessageVisible(ConversationUi.SwallowedDuration - .5f)
            && ConversationUi.SwallowedOpacity(ConversationUi.SwallowedDuration - .5f) == 1,
            "swallowed sequence starts with one second of black without text");
        foreach (float elapsed in new[] { 1f, 2f, 3f, 3.99f })
            Check(ConversationUi.SwallowedMessageVisible(ConversationUi.SwallowedDuration - elapsed)
                && ConversationUi.SwallowedOpacity(ConversationUi.SwallowedDuration - elapsed) == 1,
                "centered message holds on solid black for the next three seconds");
        Check(!ConversationUi.SwallowedMessageVisible(.6f) && ConversationUi.SwallowedOpacity(.6f) is > 0 and < 1
            && ConversationUi.SwallowedOpacity(0) == 0, "first island fades in after the message disappears");
        var flow = new GameFlow();
        Check(flow.Screen == GameScreen.Title && flow.Confirm(false) == MenuAction.NewGame, "boot opens title rather than gameplay");
        flow.Navigate(1, false); Check(flow.Confirm(false) == MenuAction.Controls, "no save skips Continue but offers controls");
        flow.ShowControls();
        Check(!flow.ControlsFromGame && GameMenus.FlightDescription(flow.ControlsFromGame && true) == "？？？",
            "title controls hide flight instructions even with a flight-enabled save");
        flow.Back(); Check(flow.Screen == GameScreen.Title && flow.Selected == 2, "title controls return to title without starting a game");
        flow.Navigate(1, false); Check(flow.Confirm(false) == MenuAction.Exit, "fourth title entry exits");
        flow.Navigate(1, false); Check(flow.Selected == 0, "four title entries wrap correctly");
        flow.ToTitle(true); Check(flow.Confirm(true) == MenuAction.Continue, "valid save initially focuses Continue");
        flow.Start(); flow.Pause(); Check(flow.Screen == GameScreen.Paused && flow.Confirm(true) == MenuAction.Resume, "Enter pause offers resume");
        flow.Navigate(1, true); Check(flow.Confirm(true) == MenuAction.Controls, "pause offers controls");
        flow.ShowControls();
        Check(flow.ControlsFromGame && GameMenus.FlightDescription(flow.ControlsFromGame && true).Contains("飛行")
            && GameMenus.FlightDescription(flow.ControlsFromGame && false) == "？？？",
            "pause controls still reveal flight instructions only after acquiring flight");
        flow.Back(); Check(flow.Screen == GameScreen.Paused, "controls return to pause");
        flow.Navigate(1, true); Check(flow.Confirm(true) == MenuAction.Title, "pause offers return to title");
        Check(GameMenus.FlightDescription(false) == "？？？" && GameMenus.FlightDescription(true).Contains("飛行"), "flight instructions depend on the acquired ability");
        var pulse = Enumerable.Range(0, 121).Select(i => GameMenus.AbilityOpacity(ItemEffect.Flight, true, i / 100.0)).ToArray();
        Check(pulse.Min() >= .2f && pulse.Max() <= 1 && pulse.Max() - pulse.Min() > .6f, "flight icon clearly pulses without vanishing");
        Check(Enumerable.Range(0, 121).All(i => GameMenus.AbilityOpacity(ItemEffect.Flight, false, i / 100.0) == 1
            && GameMenus.AbilityOpacity(ItemEffect.Jump, true, i / 100.0) == 1
            && GameMenus.AbilityOpacity(ItemEffect.Dash, true, i / 100.0) == 1), "grounded flight icon and other abilities remain steady");
        string path = Path.Combine(AppContext.BaseDirectory, "caero-auto-" + Guid.NewGuid() + ".json");
        try
        {
            Check(SaveStore.Load(path) == null, "Continue is unavailable without a save");
            using var world = new World(upload: false);
            var player = new Explorer(); player.GoTo(world.Views[0]); var auto = new AutoSave(path);
            Check(auto.Flush(player, world), "new game immediately creates an autosave");
            Check(SaveStore.Load(path)!.WorldSeconds == 0, "new game starts at dawn");
            world.Clock.Advance(1054.125);
            var item = SpecialContent.Data.Objects.First(d => d.Effect == ItemEffect.Dash);
            world.Progress.Collect(item); Check(auto.Flush(player, world), "pickup flush persists inventory");
            var saved = SaveStore.Load(path)!;
            Check(saved.WorldSeconds == world.Clock.Seconds, "autosave persists full world time across cycles");
            Check(saved.Progress!.Collected.SequenceEqual(new[] { item.Id }), "actual save file contains collected stable ID");
            using (var continued = new World(upload: false, seed: saved.Seed))
            {
                continued.Progress.Restore(saved.Progress);
                continued.Clock.ToggleSpeed(); continued.Clock.Restore(saved.WorldSeconds);
                Check(continued.Clock.State == world.Clock.State && continued.Clock.TimeScale == 1,
                    "Continue restores sun, moon and lighting at normal speed");
                continued.Clock.Advance(.25);
                Check(continued.Clock.Seconds == saved.WorldSeconds + .25, "restored clock continues advancing");
                Check(continued.Progress.AbilityCount(ItemEffect.Dash) == 1 && !continued.Progress.Collect(item), "Continue restores ability and rejects re-collection");
                var prop = continued.Sectors.SelectMany(s => s.SpecialObjects).Single(o => o.Definition.Id == item.Id);
                Vector3 eye = prop.Foot + prop.LocalFacing * 2.19f + Vector3.UnitY * Explorer.EyeHeight;
                Check(SpecialInteraction.Target(continued, eye, Vector3.Normalize(prop.ExaminePoint - eye)) != prop, "collected bottle cannot be targeted after disk reload");
            }
            player.Position += Vector3.UnitX * .5f;
            auto.Update(9, player, world); Check(SaveStore.Load(path)!.X == saved.X, "autosave does not write every frame");
            auto.Update(1, player, world); saved = SaveStore.Load(path)!;
            Check(Math.Abs(saved.X - player.Position.X) < .001f, "ten-second autosave records current position");
            world.BeginWarp(30, 30, Vector3.UnitZ);
            world.Clock.Advance(11);
            player.Position = new(55, 300, 55); auto.Update(11, player, world); auto.Flush(player, world);
            Check(SaveStore.Load(path)!.X == saved.X && SaveStore.Load(path)!.Y == saved.Y, "pause/quit during warp retains last safe position and inventory");
            Check(SaveStore.Load(path)!.WorldSeconds == world.Clock.Seconds, "safe warp save still records current time");
            world.Teleport(player, new(0, 0, world.Views[0].Position), radius: 0);
            world.Progress.Restore(null); auto.Flush(player, world);
            Check(SaveStore.Load(path)!.Progress!.Collected.Length == 0, "new-game progress resets rather than inheriting collected items");
            File.WriteAllText(path, "{\"X\":0,\"Y\":2,\"Z\":0,\"Yaw\":0,\"Pitch\":0,\"Flying\":false}");
            Check(SaveStore.Load(path) is { WorldSeconds: 0 }, "legacy saves without time default to dawn");
            double twilight = Math.Asin(.15 / .8) * DayNightCycle.Period / Math.Tau;
            double margin = twilight + 5;
            foreach (double midpoint in new[] { 0.0, 300, 600, 900, 1200, 60300 })
            foreach (double offset in new[] { -150.0, -margin - .01, -margin, -margin + .01,
                -twilight, -twilight + .01, -1, -.01, 0, .01, 1, twilight - .01, twilight,
                margin - .01, margin, margin + .01, 150 })
            {
                double seconds = midpoint + offset;
                if (seconds < 0) continue;
                double expected = offset >= -margin && offset < 0 ? midpoint - margin
                    : offset >= 0 && offset <= margin ? midpoint + margin : seconds;
                Check(SaveStore.Save(saved with { WorldSeconds = seconds }, path), "save day, dusk, night and dawn");
                Check(SaveStore.Load(path)!.WorldSeconds == seconds, "save retains original time until resume");
                var restored = new DayNightCycle(); restored.Restore(SaveStore.Load(path)!.WorldSeconds);
                Check(Math.Abs(restored.Seconds - expected) < .000001,
                    "twilight plus five-second margins snaps before/after according to midpoint; other times stay unchanged");
                if (expected != seconds)
                    Check(restored.State.Night is 0 or 1
                        && restored.State.IsNight == DayNightCycle.At(seconds).IsNight,
                        "twilight resume preserves the midpoint side with fully settled lighting");
                restored.Restore(restored.Seconds);
                Check(Math.Abs(restored.Seconds - expected) < .000001, "repeated resumes do not accumulate a time shift");
            }
            Check(SaveStore.Save(saved with { GeneratorVersion = 1, WorldSeconds = 450.125 }, path)
                && SaveStore.Load(path) is { WorldSeconds: 450.125 }, "world migration preserves saved time");
            File.WriteAllText(path, "{\"X\":0,\"Y\":2,\"Z\":0,\"WorldSeconds\":-1}");
            Check(SaveStore.Load(path) == null, "negative saved time is rejected");
            File.WriteAllText(path, "invalid-json"); Check(SaveStore.Load(path) == null, "invalid saves cannot enable Continue");
        }
        finally { File.Delete(path); if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
        Console.WriteLine("MENUS/AUTOSAVE: title options, pause/controls/resume, disk persistence, duplicate pickup rejection, periodic saves, safe warp saves and new-game reset passed.");
    }

    private static void CheckTransitions()
    {
        foreach (var action in new[] { MenuAction.NewGame, MenuAction.Continue, MenuAction.Title })
        {
            var transition = new ScreenTransition();
            Check(!transition.Active && transition.Opacity == 0, "idle transition leaves screen visible");
            transition.Begin(action); transition.Update(.5f);
            Check(transition.Opacity is > 0 and < 1 && !transition.Loading && !transition.ReadyToSwitch,
                "source fades before switching or loading");
            transition.Begin(MenuAction.Exit);
            Check(transition.Destination == action, "repeated input cannot replace an active transition");
            transition.Update(.5f);
            Check(transition.Opacity == 1 && !transition.ReadyToSwitch, "black must be presented before synchronous loading");
            Check(transition.Loading == (action != MenuAction.Title), "Loading appears only for game entry");
            transition.FramePresented();
            if (action == MenuAction.Title)
            {
                transition.Update(1.99f);
                Check(!transition.ReadyToSwitch && transition.Opacity == 1, "ending holds full black for two seconds");
                transition.Update(.02f);
            }
            Check(transition.ReadyToSwitch, "destination may now be prepared");
            transition.Reveal();
            Check(transition.Opacity == 1 && !transition.Loading && !transition.ReadyToSwitch, "destination starts hidden without Loading");
            transition.Update(.5f);
            Check(transition.Active && transition.Opacity is > 0 and < 1, "destination gradually fades in");
            transition.Update(.5f);
            Check(!transition.Active && transition.Opacity == 0, "input is released only after the fade completes");
        }
        Console.WriteLine("TRANSITIONS: New Game/Continue fade, presented Loading frame, two-second ending hold and destination fade passed.");
    }

    internal static void Capture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "caero-menu-render-" + Guid.NewGuid() + ".json");
        WindowHost.Open();
        try
        {
            using var text = new ConversationUi(); using var menus = new GameMenus(text); var flow = new GameFlow();
            var art = ContentAssets.LoadImage("title.png");
            Check(art.Width == 1440 && art.Height == 900, "title art matches default drawing dimensions"); Raylib.UnloadImage(art);
            string folder = Path.Combine(SaveStore.DirectoryPath, "Captures"); Directory.CreateDirectory(folder);
            void Shot(string name, Action draw)
            {
                for (int i = 0; i < 3; i++) { Raylib.BeginDrawing(); draw(); Raylib.EndDrawing(); }
                var image = Raylib.LoadImageFromScreen();
                try { Check(Raylib.ExportImage(image, Path.Combine(folder, "menu-" + name + ".png")), "native menu capture"); }
                finally { Raylib.UnloadImage(image); }
            }
            Shot("title-no-save", () => menus.Draw(flow, false));
            Shot("title-new-game-warning", () => menus.Draw(flow, true));
            flow.ToTitle(true); Shot("title-continue", () => menus.Draw(flow, true));
            menus.Cleared = true;
            Shot("title-cleared", () => menus.Draw(flow, true));
            Shot("title-cleared-hover", () =>
            {
                menus.Draw(flow, true);
                Rectangle trophy = GameMenus.ClearAchievementBounds(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                menus.DrawClearAchievement(new(trophy.X + trophy.Width / 2, trophy.Y + trophy.Height / 2));
            });
            menus.Cleared = false;
            var transition = new ScreenTransition(); transition.Begin(MenuAction.Continue); transition.Update(.5f);
            Shot("transition-title-out", () => { menus.Draw(flow, true); transition.Draw(text); });
            transition.Update(.5f);
            Shot("transition-loading", () => { Raylib.ClearBackground(Color.Black); transition.Draw(text); });
            transition.FramePresented();
            flow.Select(2, true); flow.ShowControls(); Shot("title-controls-locked", () => menus.Draw(flow, true, true)); flow.Back();
            using (var session = new GameSession(1701, null, false, path))
            {
                session.Player.SetCaptured(false);
                Shot("play-empty", () => session.Draw(text, true));
                transition.Reveal(); transition.Update(.5f);
                Shot("transition-game-in", () => { session.Draw(text, true); transition.Draw(text); });
                transition.Update(.5f);
                transition.Begin(MenuAction.Title); transition.Update(.5f);
                var ending = new EndingSequence(); ending.Update(20);
                Shot("transition-ending-out", () => { session.Draw(text, true); text.DrawEnding(ending); transition.Draw(text); });
                transition.Update(.5f);
                Shot("transition-ending-black", () => { Raylib.ClearBackground(Color.Black); transition.Draw(text); });
                transition.FramePresented(); transition.Update(2); transition.Reveal(); transition.Update(.5f);
                flow.ToTitle(true);
                Shot("transition-title-in", () => { menus.Draw(flow, true); transition.Draw(text); });
                Vector3 entry = session.Player.Position;
                session.World.Predator.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Hunting);
                session.Player.Position = session.World.Predator.Mouth + Vector3.UnitY * .9f;
                session.Update(.01f);
                Check(session.Player.Position == session.World.Views[0].Position,
                    "live game session returns to the first island after being swallowed");
                Shot("swallowed-black", () => session.Draw(text, true));
                Vector3 respawnPosition = session.Player.Position;
                session.Update(1.1f); Shot("swallowed-message", () => session.Draw(text, true));
                session.Update(2.8f); Shot("swallowed-message-hold", () => session.Draw(text, true));
                session.Update(.7f); Shot("swallowed-message-fade", () => session.Draw(text, true));
                Check(session.Player.Position == respawnPosition && session.Respawning,
                    "player stays at the first island throughout the respawn sequence");
                session.Update(3);
                foreach (var kind in new[] { SpecialKind.Item, SpecialKind.Inscription })
                {
                    var prop = session.World.Sectors.SelectMany(s => s.SpecialObjects).First(p => p.Definition.Kind == kind);
                    session.Player.GoTo(new("PROMPT", prop.Foot + prop.LocalFacing * 2.19f + Vector3.UnitY * Explorer.EyeHeight, prop.ExaminePoint));
                    Shot("prompt-" + kind, () => session.Draw(text, true));
                    if (kind == SpecialKind.Item)
                    {
                        session.Examine();
                        Check(session.Conversation.AwaitingChoice && !session.World.Progress.Collected(prop.Definition.Id),
                            "examining a potion does not acquire it before confirmation");
                        Shot("potion-question", () => session.Draw(text, true));
                        session.Conversation.SelectAnswer(false); session.Conversation.Advance();
                        Check(!session.World.Progress.Collected(prop.Definition.Id), "No keeps the potion available in play");
                        session.Examine(); session.Conversation.Advance(); session.Conversation.Update(100);
                        Check(session.Conversation.Document == prop && session.World.Progress.Collected(prop.Definition.Id)
                            && SaveStore.Load(path)!.Progress!.Collected.Contains(prop.Definition.Id),
                            "Yes immediately autosaves the pickup and keeps the original result pages");
                        Shot("potion-result", () => session.Draw(text, true)); session.Conversation.Close();
                    }
                }
                var person = session.World.People.First();
                Vector3 facing = new(MathF.Sin(person.Yaw), 0, MathF.Cos(person.Yaw));
                session.Player.GoTo(new("PROMPT", person.LookPoint + facing * 2.5f + Vector3.UnitY * .3f, person.LookPoint));
                Shot("prompt-person", () => session.Draw(text, true));
                session.Player.GoTo(new("ENTRY", entry, entry - Vector3.UnitZ));
                foreach (var d in SpecialContent.Data.Objects.Where(d => d.Kind == SpecialKind.Item)) session.World.Progress.Collect(d);
                session.AutoSave.Flush(session.Player, session.World);
                Shot("play-abilities", () => session.Draw(text, true));
                var inscription = session.World.Sectors.SelectMany(s => s.SpecialObjects).First(p => p.Definition.Kind == SpecialKind.Inscription);
                session.Player.GoTo(new("FLIGHT PROMPT", inscription.Foot + inscription.LocalFacing * 2.19f
                    + Vector3.UnitY * Explorer.EyeHeight, inscription.ExaminePoint));
                session.Player.Flying = true; session.Examine();
                Check(!session.Conversation.Active, "E cannot examine during flight in the game session");
                Shot("flight-abilities-bright", () => { session.Draw(text, false); GameMenus.DrawAbilities(session.World.Progress, true, 0); });
                Shot("flight-abilities-dim", () => { session.Draw(text, false); GameMenus.DrawAbilities(session.World.Progress, true, .6); });
                session.Player.Flying = false; session.Examine(); session.Conversation.Update(100);
                Check(session.Conversation.Document == inscription, "E works again after flight ends");
                Shot("message-inscription-no-icons", () => session.Draw(text, true)); session.Conversation.Close();
                var observer = session.World.People.Single(p => p.SpecialId == "island-1-05");
                Vector3 observerFacing = new(MathF.Sin(observer.Yaw), 0, MathF.Cos(observer.Yaw));
                session.Player.GoTo(new("FLIGHT SPEAKER", observer.Position - observerFacing * 2.19f
                    + Vector3.UnitY * Explorer.EyeHeight, observer.LookPoint));
                session.Player.Flying = true; session.Examine();
                Check(!session.Conversation.Active && !observer.Talking, "E cannot talk during flight");
                session.Player.Flying = false; session.Examine(); session.Conversation.Update(100);
                Check(session.Conversation.Speaker == observer, "E restores conversation after flight");
                Shot("message-person-no-icons", () => session.Draw(text, true)); session.Conversation.Close();
                session.Conversation.Confirm("ワープ装置", "ワープします。", () => { });
                Shot("message-choice-no-icons", () => session.Draw(text, true)); session.Conversation.Close();
                Shot("after-message-icons", () => session.Draw(text, true));
                flow.Start(); flow.Pause(); session.Pause();
                Vector3 position = session.Player.Position; var sky = session.Renderer.Sky;
                var people = session.World.People.Select(p => p.Animation).ToArray();
                session.Update(5);
                Check(session.Player.Position == position && session.Renderer.Sky.Equals(sky)
                    && session.World.People.Select(p => p.Animation).SequenceEqual(people), "pause freezes player, sky and people");
                Shot("pause", () => { session.Draw(text, false); menus.Draw(flow, true); });
                flow.ShowControls(); Shot("controls", () => { session.Draw(text, false); menus.Draw(flow, true, session.World.Progress.CanFly); });
                session.Resume(); session.Player.SetCaptured(false);
                session.Update(.02f);
                Check(!session.Renderer.Sky.Equals(sky), "resume advances game time again");
            }
            var saved = SaveStore.Load(path)!;
            using (var continued = new GameSession(1, saved, false, path))
            {
                continued.Player.SetCaptured(false);
                Check(continued.World.Seed == 1701 && continued.World.Progress.Snapshot().Collected.Length == 5,
                    "Continue uses saved seed and all saved abilities");
                var resumedClock = new DayNightCycle(); resumedClock.Restore(saved.WorldSeconds);
                Check(continued.Renderer.Sky == resumedClock.State
                    && SaveStore.Load(path)!.WorldSeconds == resumedClock.Seconds,
                    "live Continue restores renderer time before its initial autosave");
                Shot("continued-abilities", () => continued.Draw(text, true));
            }
            using (var fresh = new GameSession(1701, null, false, path))
            {
                fresh.Player.SetCaptured(false);
                Check(fresh.World.Progress.Snapshot().Collected.Length == 0 && SaveStore.Load(path)!.Progress!.Collected.Length == 0,
                    "New Game replaces autosave with clean inventory");
                Check(fresh.Renderer.Sky.Seconds == 0 && SaveStore.Load(path)!.WorldSeconds == 0,
                    "live New Game resets the saved clock to dawn");
            }
            Raylib.SetWindowSize(640, 360); flow.ToTitle(true);
            Shot("title-small", () => menus.Draw(flow, true));
            menus.Cleared = true;
            Shot("title-cleared-hover-small", () =>
            {
                menus.Draw(flow, true);
                Rectangle trophy = GameMenus.ClearAchievementBounds(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                menus.DrawClearAchievement(new(trophy.X + trophy.Width / 2, trophy.Y + trophy.Height / 2));
            });
            menus.Cleared = false;
            flow.Select(0, true); Shot("title-small-warning", () => menus.Draw(flow, true));
            flow.Start(); flow.Pause(); flow.ShowControls(); Shot("controls-small", () => menus.Draw(flow, true));
            var dialogue = new Conversation();
            foreach (var speech in Dialogue.Lines)
            {
                var speaker = new Resident { Id = speech.Id, Profile = speech.Id, Anchor = Vector3.Zero, Patrol = Vector3.Zero,
                    Pose = speech.Pose, Age = speech.Age, Female = speech.Gender == "female", Scale = 1, Phase = 0 };
                dialogue.Start(speaker);
                for (int page = 0; page < 3; page++)
                {
                    dialogue.Update(100);
                    Shot($"dialogue-{speech.Id:00}-{page + 1}", () => { Raylib.ClearBackground(new(20, 32, 38, 255)); text.Draw(dialogue, null); });
                    dialogue.Advance();
                }
                Check(!dialogue.Active && !speaker.Talking, "all twenty conversations close after three pages");
            }
            Console.WriteLine("MENU RENDER: Japanese title/pause/controls, text-free play, ability icons, frozen simulation, Continue/New Game and 640x360 layout passed; " + folder);
        }
        finally { File.Delete(path); if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); Raylib.CloseWindow(); }
    }
}
