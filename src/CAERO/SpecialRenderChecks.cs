using System.Numerics;
using Raylib_cs;

namespace Caero;

// Native render captures exercise Japanese glyph loading, each interaction UI,
// collectible visibility and the ending without loading or writing player saves.
internal static class SpecialRenderChecks
{
    internal static void Run(int number)
    {
        var (x, z) = SpecialChecks.Address(number);
        WindowHost.Open();
        try
        {
            using var world = new World(seed: 1701, originX: x, originZ: z);
            using var renderer = new Renderer(world, 12345);
            using var ui = new ConversationUi();
            var sector = world.Sectors.Single(s => s.X == x && s.Z == z);
            var session = new Conversation(); var player = new Explorer();
            var ending = new EndingSequence();
            string folder = Path.Combine(SaveStore.DirectoryPath, "Captures"); Directory.CreateDirectory(folder);
            void Capture(string label, Vector3 eye, Vector3 point, SpecialObject? prop = null, Resident? person = null,
                float artTime = 0, double cycleTime = 150)
            {
                player.GoTo(new(label, eye, point));
                for (int frame = 0; frame < 4; frame++)
                {
                    Raylib.BeginDrawing(); renderer.Render(world, player.Camera, artTime, cycleTime);
                    ui.Draw(session, person);
                    if (!session.Active) ui.DrawSpecial(prop);
                    if (world.Progress.Escaped) ui.DrawEnding(ending);
                    Raylib.EndDrawing();
                }
                var image = Raylib.LoadImageFromScreen();
                try
                {
                    if (!Raylib.ExportImage(image, Path.Combine(folder, $"special-{number}-{label}.png")))
                        throw new IOException("Special content capture failed");
                }
                finally { Raylib.UnloadImage(image); }
            }
            if (number == 90263)
                Capture("entrance", sector.Transform(new(22, Explorer.EyeHeight, 12)), sector.Transform(new(22, 1.25f, 28)));
            if (number == 7)
                Capture("entrance", sector.Transform(new(22, Explorer.EyeHeight, 6)), sector.Transform(new(22, 1.25f, 29)));
            if (number == 227)
            {
                Capture("west-entrance", sector.Transform(new(8, Explorer.EyeHeight, 12.5f)), sector.Transform(new(22, 1.25f, 12.5f)));
                Capture("interior", sector.Transform(new(22, Explorer.EyeHeight, 15)), sector.Transform(new(22, 1.25f, 29)));
            }
            if (number == 1601)
                Capture("wall-view", sector.Transform(new(23, Explorer.EyeHeight, 22)), sector.Transform(new(30, 1.25f, 22)));
            if (number is 1 or 3)
            {
                var roofRoute = sector.Routes.Single(r => r.Name == "hall stair and roof");
                Vector3 landing = roofRoute.Points[1];
                var inscription = sector.SpecialObjects.Single(o => o.Definition.Surface == SpecialSurface.HallRoof
                    && o.Definition.Kind == SpecialKind.Inscription);
                Capture("roof-stair-view", landing + Vector3.UnitY * Explorer.EyeHeight, inscription.Point);
            }
            foreach (var prop in sector.SpecialObjects)
            {
                if ((prop.Chunk?.Inscription == true) != (prop.Definition.Kind == SpecialKind.Inscription))
                    throw new InvalidOperationException("Only inscriptions must use the faint inscription glow");
                if (prop.Definition.Kind == SpecialKind.Inscription)
                {
                    Vector3 view = prop.Foot + prop.LocalFacing * 7 + Vector3.UnitY * 2.1f;
                    Capture(prop.Definition.Id + "-glow-day", view, prop.Point);
                    Capture(prop.Definition.Id + "-glow-night", view, prop.Point, cycleTime: 450);
                }
                if (prop.Definition.Kind == SpecialKind.Exit)
                {
                    if (prop.Chunk?.ExitRelic != true || prop.Chunk.SmoothMaterial != true)
                        throw new InvalidOperationException("Exit relic must have its own luminous material");
                    Vector3 view = prop.Foot + prop.LocalFacing * 5 + Vector3.UnitY * 2;
                    Capture("exit-corridor", sector.Transform(new(22, 6 + Explorer.EyeHeight, 31.7f)), prop.Point);
                    if (prop.PedestalChunk == null || !world.Chunks.Contains(prop.PedestalChunk))
                        throw new InvalidOperationException("Exit corridor floor is missing from the rendered scene");
                    Capture("exit-relic-day", view, prop.Point);
                    Capture("exit-relic-night-bright", view, prop.Point, artTime: 0, cycleTime: 450);
                    Capture("exit-relic-night-dim", view, prop.Point, artTime: 3, cycleTime: 450);
                }
                if (prop.Definition.Surface == SpecialSurface.TowerLanding)
                    Capture(prop.Definition.Id + "-landing", prop.Foot + prop.LocalFacing * 6 + Vector3.UnitY * 2.3f, prop.Point);
                Vector3 eye = prop.Foot + prop.LocalFacing * 2.19f + Vector3.UnitY * Explorer.EyeHeight;
                Capture(prop.Definition.Id + "-object", eye, prop.ExaminePoint, prop);
                SpecialInteraction.Examine(world, player, prop, session); session.Update(100);
                if (!world.Progress.Escaped && !session.Active)
                    throw new InvalidOperationException("Render capture failed to activate " + prop.Definition.Id);
                if (prop.Definition.Kind == SpecialKind.Item)
                {
                    Capture(prop.Definition.Id + "-question", eye, prop.ExaminePoint);
                    if (world.Progress.Collected(prop.Definition.Id) || prop.Chunk == null || !world.Chunks.Contains(prop.Chunk))
                        throw new InvalidOperationException("Potion must remain visible until Yes");
                    session.SelectAnswer(false); session.Advance();
                    if (session.Active || world.Progress.Collected(prop.Definition.Id))
                        throw new InvalidOperationException("No must leave the potion available");
                    SpecialInteraction.Examine(world, player, prop, session); session.Advance(); session.Update(100);
                }
                if (prop.Definition.Kind == SpecialKind.Item && (prop.Chunk == null || world.Chunks.Contains(prop.Chunk)))
                    throw new InvalidOperationException("Collected relic is still drawn");
                if (prop.Definition.Kind == SpecialKind.Item && (prop.PedestalChunk == null || !world.Chunks.Contains(prop.PedestalChunk)))
                    throw new InvalidOperationException("Collected potion lost its pedestal");
                if (prop.Definition.Kind == SpecialKind.Item && (prop.Chunk?.SmoothMaterial != true || prop.PedestalChunk?.SmoothMaterial != false))
                    throw new InvalidOperationException("Potion and stone pedestal must use separate materials");
                Capture(prop.Definition.Id + "-read", eye, prop.ExaminePoint); session.Close();
                if (prop.Definition.Kind == SpecialKind.Exit)
                {
                    SpecialInteraction.Examine(world, player, prop, session);
                    session.SelectAnswer(false); session.Advance();
                    if (world.Progress.Escaped || session.Active) throw new InvalidOperationException("No must cancel escape");
                    SpecialInteraction.Examine(world, player, prop, session);
                    session.Advance();
                    if (!world.Progress.Escaped) throw new InvalidOperationException("Yes must begin escape");
                    foreach (var (label, seconds) in new[] { ("white-fade", 1f), ("white", 2.2f), ("message", 4.2f),
                        ("image-fade", 6.5f), ("image-wait", 8.5f), ("return-button", 11.5f) })
                    {
                        ending.Update(seconds - ending.Elapsed);
                        Capture("ending-" + label, eye, prop.ExaminePoint);
                    }
                }
            }
            if (world.Progress.Escaped) world.Progress.Restore(world.Progress.Snapshot() with { Escaped = false, ExitId = null });
            foreach (var person in sector.People.Where(p => p.SpecialId != null))
            {
                Vector3 forward = new(MathF.Sin(person.Yaw), 0, MathF.Cos(person.Yaw));
                if (person.SpecialId == "island-3-03")
                    Capture(person.SpecialId + "-under-stair", sector.Transform(new(23, Explorer.EyeHeight, 23)), person.LookPoint);
                if (number == 227)
                    Capture(person.SpecialId + "-surroundings", person.Position + forward * 7 + Vector3.UnitY * 2.5f, person.LookPoint);
                if (person.SpecialId == "island-1169587-02")
                    Capture(person.SpecialId + "-corner", person.Position + forward * 6 + Vector3.UnitY * 2, person.LookPoint);
                Vector3 hemPoint = person.Position + Vector3.UnitY * person.Scale * .16f;
                Capture(person.SpecialId + "-robe-detail", hemPoint + forward * (1.6f * person.Scale)
                    + Vector3.UnitY * (.55f * person.Scale), hemPoint);
                if (SpecialContent.ById[person.SpecialId!].LeanAgainstWall)
                {
                    Vector3 side = new(forward.Z, 0, -forward.X);
                    Capture(person.SpecialId + "-wall-seat", person.Position + forward * 4 + side * 2 + Vector3.UnitY * 1.8f,
                        person.LookPoint);
                }
                if (person.SpecialId == "island-1-05")
                    Capture(person.SpecialId + "-overlook", person.Position - forward * 8 + new Vector3(-3, 3.5f, 3),
                        person.Position + forward * 6 + Vector3.UnitY);
                if (person.SpecialId is "island-641-06" or "island-1601-06")
                    Capture(person.SpecialId + "-overlook", person.Position - forward * 4 + new Vector3(forward.Z * 2, 2.4f, -forward.X * 2),
                        person.Position + forward * 10 + Vector3.UnitY);
                session.Start(person); session.Update(100);
                Capture(person.SpecialId + "-talk", person.LookPoint + forward * (person.SpecialId == "island-1-05" ? -2.5f : 2.5f)
                    + Vector3.UnitY * .3f, person.LookPoint);
                if (person.Speech.Choice != null)
                {
                    Vector3 eye = person.LookPoint + forward * 2.5f + Vector3.UnitY * .3f;
                    Capture(person.SpecialId + "-rooftop", person.Position + forward * 6 + Vector3.UnitY * 3, person.LookPoint);
                    while (session.Page < person.Speech.Pages.Length - 1) { session.Update(100); session.Advance(); }
                    session.Update(100);
                    Capture(person.SpecialId + "-choice", eye, person.LookPoint);
                    session.Update(Conversation.ChoiceInputDelay);
                    ConversationInput.Handle(session, key => key == KeyboardKey.Right);
                    Capture(person.SpecialId + "-choice-no", eye, person.LookPoint);
                    ConversationInput.Handle(session, key => key == KeyboardKey.Enter); session.Update(100);
                    Capture(person.SpecialId + "-answer-no", eye, person.LookPoint);
                    if (person.Speech.Choice.NoChoice != null)
                    {
                        while (session.Active && !session.AwaitingChoice)
                        { session.Update(100); if (!session.AwaitingChoice) session.Advance(); }
                        Capture(person.SpecialId + "-second-choice", eye, person.LookPoint);
                        session.Update(Conversation.ChoiceInputDelay);
                        ConversationInput.Handle(session, key => key == KeyboardKey.Right);
                        ConversationInput.Handle(session, key => key == KeyboardKey.Enter); session.Update(100);
                        Capture(person.SpecialId + "-second-answer-no", eye, person.LookPoint);
                        session.Start(person);
                        while (session.Page < person.Speech.Pages.Length - 1) { session.Update(100); session.Advance(); }
                        session.Update(100); session.SelectAnswer(false); session.Advance();
                        session.Update(100);
                        for (int page = 0; page < person.Speech.Choice.NoPages.Length - 1; page++)
                        { session.Advance(); session.Update(100); }
                        session.SelectAnswer(true); session.Advance(); session.Update(100);
                        Capture(person.SpecialId + "-second-answer-yes", eye, person.LookPoint);
                    }
                    session.Start(person);
                    while (session.Page < person.Speech.Pages.Length - 1) { session.Update(100); session.Advance(); }
                    session.Update(100); session.Update(Conversation.ChoiceInputDelay);
                    ConversationInput.Handle(session, key => key == KeyboardKey.Enter); session.Update(100);
                    Capture(person.SpecialId + "-answer-yes", eye, person.LookPoint);
                }
                session.Close();
            }
            Console.WriteLine($"SPECIAL RENDER: island {number}, props/people, Japanese text and interaction captures passed; {folder}");
        }
        finally { Raylib.CloseWindow(); }
    }
}
