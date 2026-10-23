using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed class GameSession : IDisposable
{
    internal World World { get; }
    internal Explorer Player { get; } = new();
    internal Renderer Renderer { get; }
    internal AudioDirector Audio { get; }
    internal Conversation Conversation { get; } = new();
    internal AutoSave AutoSave { get; }
    internal EndingSequence Ending { get; } = new();
    internal AltitudeBoundary Boundary { get; } = new();
    internal bool ReturnToTitleRequested { get; private set; }
    private bool endingStarted;
    private float swallowed;
    internal bool Respawning => swallowed > 0 || Boundary.Active;
    private bool paused;
    internal GameSession(int seed, SavedView? saved, bool sound, string? savePath = null)
    {
        World = new(seed: saved?.Seed ?? seed, start: saved == null ? null : new Vector3(saved.X, saved.Y, saved.Z),
            originX: saved?.IslandX ?? 0, originZ: saved?.IslandZ ?? 0);
        Renderer = new(World); Audio = new(sound); AutoSave = new(savePath);
        World.OriginShifted += Audio.ShiftOrigin; World.ViewTeleported += Audio.ResetListener;
        Player.GoTo(new("ENTRY", World.EntryPosition, World.EntryPosition + World.Views[0].Target - World.Views[0].Position));
        if (saved != null)
        {
            World.Progress.Restore(saved.Progress); Player.Restore(saved);
            if (!World.Progress.CanFly) Player.Flying = false;
            if (World.Blocked(Player.Position)) World.ReturnToStart(Player);
        }
        AutoSave.Flush(Player, World);
    }
    private void BeginEnding()
    {
        if (endingStarted) return;
        endingStarted = true; swallowed = 0;
        Conversation.Close(); Player.ClearMotionEvents(); Player.SetCaptured(false); Audio.Suspend(true);
        AutoSave.Flush(Player, World);
    }
    private void BeginRespawn()
    {
        Boundary.Reset(); Conversation.Close(); Player.ClearMotionEvents();
        Audio.ResetListener(Player.Position); Audio.Update(0, World, Player, Renderer);
        swallowed = ConversationUi.SwallowedDuration;
        AutoSave.Flush(Player, World);
    }
    private bool HandleAltitude(float dt)
    {
        if (!Boundary.Active && (World.WarpInProgress || Player.Position.Y <= AltitudeBoundary.HeightLimit)) return false;
        Conversation.Close(); Player.ClearMotionEvents();
        if (Boundary.Update(Player.Position.Y, dt))
        {
            World.ReturnToStart(Player, 10);
            BeginRespawn();
        }
        return true;
    }
    internal void Update(float dt)
    {
        if (paused) return;
        if (swallowed > 0)
        {
            swallowed = Math.Max(0, swallowed - dt);
            Player.ClearMotionEvents();
            return;
        }
        if (World.Progress.Escaped)
        {
            BeginEnding();
            if (Raylib.IsWindowFocused()) Ending.Update(dt);
            ReturnToTitleRequested = ConversationUi.PollEndingReturn(Ending);
            return;
        }
        if (HandleAltitude(dt)) return;
#if DEBUG
        if (Raylib.IsWindowFocused() && !World.Progress.Escaped && DebugIslandTravel.Poll(World, Player, Conversation))
        {
            swallowed = 0;
            AutoSave.Flush(Player, World);
            return;
        }
#endif
        Renderer.Update(dt); World.AdvanceWarp(dt, Player); World.Update(Player.Position);
        if (World.TakeWarpFailure() is { } error) Console.Error.WriteLine("WARP: " + error);
        bool reading = Conversation.Active;
        if (World.WarpInProgress)
        {
            if (World.WarpFalling) Player.Advance(dt, World, Vector3.Zero, false, false, false, false);
            else Player.ClearMotionEvents();
        }
        else if (!reading && !World.Progress.Escaped) Player.Update(dt, World);
        else Player.ClearMotionEvents();
        if (HandleAltitude(0)) return;
        if (!World.WarpInProgress && !World.Progress.Escaped && Raylib.IsWindowFocused() && World.Predator.Update(dt, World, Player))
        { BeginRespawn(); return; }
        foreach (var person in World.People) person.Update(dt, Player.Position);
        if (Conversation.Speaker is { } speaker && (!World.People.Contains(speaker) || Vector3.Distance(speaker.Position, Player.Position) > 5)) Conversation.Close();
        if (Conversation.Document is { } document && (!World.Sectors.Any(s => s.SpecialObjects.Contains(document))
            || Vector3.Distance(document.Point, Player.Position) > 5)) Conversation.Close();
        if (!World.WarpInProgress && !World.Progress.Escaped && Raylib.IsWindowFocused())
        {
            if (reading)
            {
                Conversation.Update(dt);
                ConversationInput.Poll(Conversation);
                if (World.Progress.Escaped) { BeginEnding(); return; }
            }
            else if (Player.Captured && Raylib.IsKeyPressed(KeyboardKey.E)) Examine();
        }
        Audio.Update(dt, World, Player, Renderer); AutoSave.Update(dt, Player, World);
    }
    private (Resident? Person, WarpTarget? Warp, SpecialObject? Prop) ExamineTarget()
    {
        if (Player.Flying) return (null, null, null);
        var person = Conversation.Target(World, Player.Position, Player.Forward);
        var warp = WarpTravel.Target(World, Player.Position, Player.Forward);
        var prop = SpecialInteraction.Target(World, Player.Position, Player.Forward);
        float distance = prop == null ? float.PositiveInfinity : Vector3.DistanceSquared(Player.Position, prop.ExaminePoint);
        if (prop != null && (person == null || distance < Vector3.DistanceSquared(Player.Position, person.LookPoint))
            && (warp == null || distance < Vector3.DistanceSquared(Player.Position, warp.Point)))
            return (null, null, prop);
        return warp != null ? (null, warp, null) : (person, null, null);
    }
    internal void Examine()
    {
        var (person, warp, prop) = ExamineTarget();
        if (prop != null)
        {
            SpecialInteraction.Examine(World, Player, prop, Conversation, () => AutoSave.Flush(Player, World));
            if (prop.Definition.Kind == SpecialKind.Exit) AutoSave.Flush(Player, World);
        }
        else if (warp != null)
        {
            WarpTravel.Examine(World, Player, warp, Conversation, () => AutoSave.Flush(Player, World));
        }
        else if (person != null) Conversation.Start(person);
    }
    internal void Draw(ConversationUi text, bool overlay)
    {
        Renderer.Render(World, Boundary.Distort(Player.Camera),
            warpStrength: Boundary.Active ? Boundary.Strength : null, warpTime: Boundary.Active ? Boundary.Elapsed : null,
            boundaryStrength: Boundary.Strength);
        if (!overlay) return;
        if (Boundary.Active) return;
        if (World.Progress.Escaped) { text.DrawEnding(Ending); return; }
        // Use the activation target for a single matching interaction prompt.
        var (person, warp, prop) = !Conversation.Active && !World.WarpInProgress && !World.Progress.Escaped
            ? ExamineTarget() : (null, null, null);
        text.Draw(Conversation, person);
        text.DrawWarp(warp);
        text.DrawSpecial(prop);
        if (!Conversation.Active) GameMenus.DrawAbilities(World.Progress, Player.Flying);
        text.DrawSwallowed(swallowed);
    }
    internal void Pause()
    {
        paused = true;
        AutoSave.Flush(Player, World); Player.ClearMotionEvents(); Player.SetCaptured(false); Audio.Suspend(true);
    }
    internal void Resume()
    {
        paused = false;
        Player.SetCaptured(true); Audio.Suspend(false); Audio.ResetListener(Player.Position);
    }
    public void Dispose()
    {
        AutoSave.Flush(Player, World); Conversation.Close(); Audio.Dispose(); Renderer.Dispose(); World.Dispose();
    }
}

internal static class GameApplication
{
    internal static void Run(int newGameSeed, bool sound)
    {
        WindowHost.Open(); GameSession? session = null;
        try
        {
            using var text = new ConversationUi(); using var menus = new GameMenus(text); var flow = new GameFlow();
            var transition = new ScreenTransition();
            SavedView? saved = SaveStore.Load(); bool running = true;
            flow.ToTitle(saved != null);
            var savedProgress = new AdventureProgress(); savedProgress.Restore(saved?.Progress);
            Raylib.EnableCursor();
            while (running && !Raylib.WindowShouldClose())
            {
                float dt = Math.Min(Raylib.GetFrameTime(), .05f);
                bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
                if (alt && Raylib.IsKeyPressed(KeyboardKey.Enter)) WindowHost.Toggle();
                bool changed = false;
                if (transition.Active)
                {
                    transition.Update(dt); changed = true;
                    if (transition.ReadyToSwitch)
                    {
                        if (transition.Destination == MenuAction.Title)
                        {
                            session!.Dispose(); session = null;
                            saved = SaveStore.Load(); savedProgress.Restore(saved?.Progress);
                            flow.ToTitle(saved != null); Raylib.EnableCursor();
                        }
                        else
                        {
                            // The previous frame has already presented black with Loading... .
                            session = new(newGameSeed, transition.Destination == MenuAction.Continue ? saved : null, sound);
                            flow.Start();
                        }
                        transition.Reveal();
                    }
                    else if (!transition.Active && flow.Screen == GameScreen.Playing) session!.Resume();
                }
                if (!changed && !alt && flow.Screen == GameScreen.Playing && !session!.Respawning && !session.World.Progress.Escaped
                    && !session.Conversation.Active && Raylib.IsKeyPressed(KeyboardKey.Enter))
                { session!.Pause(); flow.Pause(); changed = true; }
                if (flow.Screen != GameScreen.Playing && !changed && !alt)
                {
                    MenuAction action = menus.Input(flow, saved != null);
                    switch (action)
                    {
                        case MenuAction.NewGame:
                        case MenuAction.Continue:
                            transition.Begin(action); changed = true; break;
                        case MenuAction.Exit: running = false; break;
                        case MenuAction.Controls: flow.ShowControls(); changed = true; break;
                        case MenuAction.Resume:
                            if (flow.Screen == GameScreen.Controls) flow.Back();
                            else { flow.Start(); session!.Resume(); }
                            changed = true; break;
                        case MenuAction.Title:
                            session!.Dispose(); session = null; saved = SaveStore.Load(); savedProgress.Restore(saved?.Progress); flow.ToTitle(saved != null);
                            Raylib.EnableCursor(); changed = true; break;
                    }
                }
                if (flow.Screen == GameScreen.Playing && !changed) session!.Update(dt);
                if (!transition.Active && session?.ReturnToTitleRequested == true)
                {
                    transition.Begin(MenuAction.Title);
                }
                Raylib.BeginDrawing();
                if (transition.Opacity < 1)
                {
                    if (session != null) session.Draw(text, flow.Screen == GameScreen.Playing);
                    if (flow.Screen != GameScreen.Playing) menus.Draw(flow, saved != null, session?.World.Progress.CanFly ?? savedProgress.CanFly);
                }
                else Raylib.ClearBackground(Color.Black);
                transition.Draw(text);
                Raylib.EndDrawing();
                transition.FramePresented();
            }
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(SaveStore.DirectoryPath);
            File.WriteAllText(Path.Combine(SaveStore.DirectoryPath, "error.log"), error.ToString()); throw;
        }
        finally { session?.Dispose(); Raylib.CloseWindow(); }
    }
}
