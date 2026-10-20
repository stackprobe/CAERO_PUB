using System.Numerics;
using Raylib_cs;
using Caero;

bool captureChaos = args.Contains("--capture-chaos");
bool captureWayfinding = args.Contains("--capture-wayfinding");
bool captureIdentity = args.Contains("--capture-identity") || captureWayfinding;
long identityDistance = long.Parse(args.FirstOrDefault(a => a.StartsWith("--identity-distance="))?.Split('=')[1] ?? (captureWayfinding ? "0" : "4"));
string[]? identityCell = args.FirstOrDefault(a => a.StartsWith("--identity-cell="))?[16..].Split(',');
long identityX = identityCell == null ? identityDistance : long.Parse(identityCell[0]);
long identityZ = identityCell == null ? 0 : long.Parse(identityCell[1]);
string identityLabel = identityCell == null ? identityDistance.ToString() : $"{identityX},{identityZ}";
bool captureTowerFaces = args.Contains("--capture-tower-faces");
bool captureFrameTower = args.Contains("--capture-frame-tower");
bool captureCycle = args.Contains("--capture-cycle");
bool captureNight = args.Contains("--night");
bool capturePeople = args.Contains("--capture-people");
bool capturePredator = args.Contains("--capture-predator");
bool captureWater = args.Contains("--capture-water");
bool capture = args.Contains("--capture") || captureWayfinding || captureIdentity || captureChaos || captureTowerFaces || captureFrameTower || captureCycle || capturePeople || capturePredator || captureWater, selfTest = args.Contains("--self-test");
bool streamTest = args.Contains("--stream-test");
if (args.Contains("--building-test")) { BuildingChecks.Run(); return; }
if (args.Contains("--wayfinding-test")) { WayfindingChecks.Run(); return; }
if (args.Contains("--dialogue-test")) { DialogueChecks.Run(); return; }
if (args.Contains("--resident-test")) { ResidentChecks.Run(); return; }
if (args.Contains("--layout-test")) { ChaosChecks.Layouts(); return; }
if (args.Contains("--predator-test")) { PredatorChecks.Run(); return; }
if (args.Contains("--origin-test")) { FloatingOriginChecks.Run(); return; }
if (args.Contains("--audio-test") || args.Contains("--audio-smoke-test"))
{
    AudioChecks.Run(args.Contains("--audio-smoke-test"));
    return;
}
string? seedArgument = args.FirstOrDefault(a => a.StartsWith("--seed="));
if (seedArgument != null && !int.TryParse(seedArgument[7..], out _))
    throw new ArgumentException("--seed requires a signed 32-bit integer.");
if (streamTest || args.Contains("--movement-test") || args.Contains("--surface-test"))
{
    ProceduralChecks.Run();
    return;
}
SavedView? saved = capture || selfTest ? null : SaveStore.Load();
int seed = seedArgument == null ? saved?.Seed ?? World.DefaultSeed : int.Parse(seedArgument[7..]);
if (saved?.Seed != seed) saved = null;
WindowHost.Open();
try
{
    if (selfTest) ProceduralChecks.Run();
    Vector3? start = captureIdentity || capturePredator ? Vector3.Zero : captureFrameTower ? new(192, 0, -6144) : captureTowerFaces ? new(1920, 0, 3072) : captureChaos ? new(2304, 0, 0) : saved == null ? null : new(saved.X, saved.Y, saved.Z);
    using var world = new World(seed: seed, start: start, originX: captureIdentity ? identityX : saved?.IslandX ?? 0,
        originZ: captureIdentity ? identityZ : saved?.IslandZ ?? 0);
    using var renderer = new Renderer(world, capture || selfTest ? 12345 : null);
    using var audio = new AudioDirector(!capture && !selfTest && !args.Contains("--no-audio"));
    world.OriginShifted += audio.ShiftOrigin; world.ViewTeleported += audio.ResetListener;
    using var conversationUi = new ConversationUi();
    var conversation = new Conversation();
    if (captureNight) renderer.StartAtNight();
    var explorer = new Explorer(); explorer.GoTo(new("ENTRY", world.EntryPosition, world.EntryPosition + world.Views[0].Target - world.Views[0].Position));
    if (saved != null) explorer.Restore(saved);
    if (captureIdentity || captureChaos || captureTowerFaces || captureFrameTower) explorer.GoTo(new("CHAOS REGION", world.EntryPosition, world.EntryPosition - Vector3.UnitZ));
    if (capturePredator)
    {
        world.Predator.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Patrol);
        explorer.GoTo(new("THE DEEP", new(96, -48, 122), new(96, -48, 104)));
    }
    if (!capture && !selfTest) Raylib.DisableCursor();
    var startCell = world.GlobalCell(explorer.Position);
    var origin = world.Sectors.First(s => s.X == startCell.X && s.Z == startCell.Z);
    Viewpoint V(string name, Vector3 p, Vector3 target) => new(name, origin.Transform(p), origin.Transform(target));
    Viewpoint SculptureView(float side) => new("SCULPTURE",
        origin.SculptureFoot + Vector3.Transform(new Vector3(0, 0, side * (origin.SculptureStyle == SculptureCatalog.StartStyle ? 15 : 10)),
            Matrix4x4.CreateRotationY(origin.SculptureYaw - origin.Rotation * MathF.PI / 2 + (origin.SculptureStyle == SculptureCatalog.StartStyle ? .4f : 0)))
            + new Vector3(0, origin.SculptureStyle == SculptureCatalog.StartStyle ? 6 : 5, 0),
        origin.SculptureFoot + new Vector3(0, origin.SculptureStyle == SculptureCatalog.StartStyle ? 4.5f : 3.5f, 0));
    float roofHeight = origin.HighRoof;
    float skyHeight = origin.SkyPorts.FirstOrDefault()?.Height ?? 0;
    double[] cycleTimes = [150, 294, 298, 300, 304, 310, 450, 594, 598, 600, 604, 610];
    var frameTower = captureFrameTower ? origin.FrameTowers.First() : null;
    var portraits = world.People.Where(p => world.GlobalCell(p.Anchor) == startCell).OrderBy(p => p.Profile).ToArray();
    Viewpoint Portrait(Resident p)
    {
        var forward = Vector3.Transform(new Vector3(0, 0, 1), Matrix4x4.CreateRotationY(p.Yaw));
        var point = p.LookPoint;
        return new(p.Name, point + forward * 2.8f + new Vector3(1.2f, .45f, 0), point - new Vector3(0, .25f, 0));
    }
    Viewpoint[] captures = captureWayfinding ? [
        new("COMPASS", origin.CompassCenter + new Vector3(0, 6, 3), origin.CompassCenter),
        new("ISLAND NUMBER", origin.NumberPlateCenter + origin.NumberPlateNormal * 5, origin.NumberPlateCenter)
    ] : captureWater ? new[] { .05f, .001f, 0f, -.001f, -.05f, -.6f, -6f, -42f }.Select(depth =>
        new Viewpoint($"WATERLINE {depth}", new(1.7f, World.WaterLevel + depth, 108.7f),
            new(7, World.WaterLevel + depth, 125))).Append(
        new("BOTTOMLESS", new(96, -30, 100), new(96, -64, 108))).ToArray() : captureIdentity ? [
        V("ISLAND IDENTITY", new(-65, 44, 74), new(-8, 24, 6)),
        V("WALL MATERIAL", new(7, 5, 24), new(12, 5, 22)),
        V("PAVING WEEDS", new(-5, 2.8f, 7), new(-12, .2f, -2)),
        origin.SculptureStyle < 0 ? V("SCULPTURE", new(-2, 5, 30), new(-11, 3.5f, 21))
            : SculptureView(1),
        V("LATERAL WINGS", new(-65, origin.WingBaseHeight + 16, 45), new(-39, origin.WingBaseHeight + 12, 22)),
        origin.SculptureStyle < 0 ? V("COURTYARD", new(5, 3, 20), new(-10, 3, 20))
            : SculptureView(-1)
    ] : capturePredator ? [
        new("EYES IN THE DEEP", new(96, -47.6f, 124), new(96, -47.6f, 103)),
        new("APPROACH", new(96, -47.6f, 113), new(96, -48, 103)),
        new("GIANT SILHOUETTE", new(109, -45, 100), new(96, -49, 96)),
        new("OPEN JAWS", new(96, -49.5f, 109), new(96, -49.5f, 103)),
        world.Views[0],
        new("SHALLOW BRIDGE BLUR", new(82.1f, -21.3f, 49.5f), new(82.1f, -23, 25)),
        new("DISTANT EYES", new(96, -49, 170), new(96, -49, 96)),
        new("FAR EYES", new(96, -49, 220), new(96, -49, 96))
    ] : capturePeople ? portraits.Select(Portrait).Concat(new[] { Portrait(portraits[0]) }).ToArray() : captureFrameTower ? [
        V("FRAME TOWER", new(frameTower!.X, 18, frameTower.Z + frameTower.Depth / 2 + 25), new(frameTower.X, 32, frameTower.Z)),
        V("FRAME JOINT A", new(frameTower.X - frameTower.Width / 2 - 4, 12, frameTower.Z + frameTower.Depth / 2 + 6), new(frameTower.X - frameTower.Width / 2, 12, frameTower.Z + frameTower.Depth / 2)),
        V("FRAME JOINT B", new(frameTower.X - frameTower.Width / 2 - 3.96f, 12.02f, frameTower.Z + frameTower.Depth / 2 + 6), new(frameTower.X - frameTower.Width / 2, 12, frameTower.Z + frameTower.Depth / 2)),
        V("FRAME FLOOR", new(frameTower.X, 16, frameTower.Z), new(frameTower.X - frameTower.Width / 2, 12.7f, frameTower.Z + frameTower.Depth / 2))
    ] : captureCycle ? cycleTimes.SelectMany(t =>
    {
        var sky = DayNightCycle.At(t);
        var direction = sky.Sun.Y > 0 ? sky.Sun : sky.Moon;
        Vector3 eye = new(80, 120, -80);
        return new[] { new Viewpoint($"SKY {t}", eye, eye + direction * 100),
            V($"LANTERNS {t}", new(2, 3, 12), new(-10, 2, 3)) };
    }).ToArray() : captureTowerFaces ? [
        V("TOWER GATE A", new(-8.7f, skyHeight + 1.8f, 12.5f), new(-20, skyHeight, 12.5f)),
        V("TOWER GATE B", new(-8.66f, skyHeight + 1.8f, 12.46f), new(-20, skyHeight, 12.5f)),
        V("PILLAR AND BEAM", new(-35, skyHeight + 2, 17), new(-31, skyHeight - .3f, 11)),
        V("ROOF CORNER", new(-28, roofHeight + 2, 16), new(-31, roofHeight, 11)) ] : captureChaos ? [
        V("VERTICAL CITY", new(44, roofHeight * .65f, 80), new(-10, roofHeight * .5f, 0)),
        V("FROM THE GROUND", new(0, 1.75f, 65), new(-22, roofHeight * .5f, 22)),
        V("ROOFTOP CROSSING", new(18, origin.TowerFloors * 12 + 1.75f, 12.5f), new(-22, 55, 12.5f)),
        V("SKY JUNCTION", new(0, skyHeight + 1.75f, 11), new(0, skyHeight, -192)),
        V("THE HIGH ROOF", new(-26, roofHeight + 1.75f, 12), new(60, roofHeight + 8, -50)),
        V("FLOATING COURT", new(44, roofHeight + 4, -50), new(60, roofHeight + 12, -50)),
        V("SUSPENDED SKYLINE", new(30, roofHeight + 65, 80), new(-55, roofHeight + 30, 50)),
        V("CONNECTED ISLANDS", new(88, 58, 88), new(125, 55, 0)) ] : [
        V("THE ARCHIPELAGO", new(-85, 58, 91), new(0, 7, 0)),
        V("THE ARCADE", new(0, 1.75f, 4), new(0, 7, -30)),
        V("THE HALL", new(-15, 1.75f, -20), new(23, 7, -25)),
        V("THE ROOF", new(-16, origin.HallHeight + 1.75f, -20), new(22, 22, 22)),
        V("THE TOWER", new(22, 1.75f, 24), new(18, 7, 30)),
        V("THE SEA STAIR", new(-25, -17.5f, 90), new(-25, 0, 38)),
        V("THE CROSSING", new(80, 1.75f, 0), new(192, 12, 0)),
        V("GROUNDED BRIDGE LAMP", new(0, 2.8f, 70), new(2.8f, .5f, 66)),
        V("BENEATH THE SEA", new(-25, -30, 80), new(-25, -20, 45)) ];
    bool help = true; int frame = 0; string notice = ""; float noticeTime = 0;
    float swallowed = 0;
    WorldPosition[] testStops = [new(0, 0, new(0, 50, 0)), new(3, 0, new(0, World.WaterLevel - .001f, 0)), new(-5, 4, new(0, World.WaterLevel + .001f, 0)),
        new(1_000_000_000_000, -1_000_000_000_000, new(95.9f, 50, 0)),
        new(-1_000_000_000_000, 1_000_000_000_000, new(-95.9f, 50, 0)), new(0, 0, new(0, 50, 0))];
    int testStop = 0, stableFrames = 0;
    int positionedStop = -1;
    var gpuTestTimer = System.Diagnostics.Stopwatch.StartNew();
    while (!Raylib.WindowShouldClose())
    {
        float dt = Math.Min(Raylib.GetFrameTime(), .05f);
        renderer.Update(Raylib.GetFrameTime());
        if ((Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt)) && Raylib.IsKeyPressed(KeyboardKey.Enter)) WindowHost.Toggle();
        if (Raylib.IsKeyPressed(KeyboardKey.H)) help = !help;
        if (Raylib.IsKeyPressed(KeyboardKey.N)) renderer.ToggleTimeScale();
        if (Raylib.IsKeyPressed(KeyboardKey.M)) audio.ToggleMute();
        if (Raylib.IsKeyPressed(KeyboardKey.F5))
        { notice = SaveStore.Save(explorer, world) ? "VIEW + WORLD SEED SAVED" : "SAVE FAILED: " + SaveStore.LastError; noticeTime = 4; }
        if (Raylib.IsKeyPressed(KeyboardKey.F9) && SaveStore.Load() is { } view)
        {
            if (view.Seed == world.Seed)
            {
                world.Teleport(explorer, new(view.IslandX, view.IslandZ, new(view.X, view.Y, view.Z)));
                conversation.Close(); explorer.Restore(view); notice = "VIEW RESTORED";
            }
            else notice = "SAVED SEED DIFFERS - RESTART WITHOUT --seed";
            noticeTime = 4;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.One))
        { conversation.Close(); world.ReturnToStart(explorer); }
        if (selfTest)
        {
            if (positionedStop != testStop)
            {
                world.Teleport(explorer, testStops[testStop], radius: 0);
                explorer.GoTo(new("GPU STREAM TEST", explorer.Position, explorer.Position + new Vector3(30, -30, -40)));
                if (testStop is 3 or 4)
                {
                    explorer.Flying = true;
                    int uploads = world.MeshUploadCount, installed = world.InstalledSectorCount, shifted = world.OriginShiftCount;
                    explorer.Advance(.01f, world, testStop == 3 ? Vector3.UnitX : -Vector3.UnitX, false, false, false, false, true);
                    if (world.OriginShiftCount != shifted + 1 || world.MeshUploadCount != uploads || world.InstalledSectorCount != installed)
                        throw new InvalidOperationException("GPU boundary rebase regenerated or reuploaded island meshes.");
                }
                positionedStop = testStop;
            }
            if (gpuTestTimer.Elapsed.TotalSeconds > 60) throw new TimeoutException("GPU streaming test timed out.");
        }
        world.Update(explorer.Position);
        bool wasTalking = conversation.Speaker != null;
        if (!capture && !selfTest && !wasTalking) explorer.Update(dt, world);
        else if (wasTalking) explorer.ClearMotionEvents();
        if (!capture && !selfTest && Raylib.IsWindowFocused() && world.Predator.Update(dt, world, explorer))
        { conversation.Close(); audio.ResetListener(explorer.Position); swallowed = 2.2f; }
        swallowed = Math.Max(0, swallowed - dt);
        foreach (var person in world.People) person.Update(capture ? 0 : dt);
        Resident? target = Conversation.Target(world, explorer.Position, explorer.Forward);
        if (conversation.Speaker is { } current && (!world.People.Contains(current) || Vector3.Distance(current.Position, explorer.Position) > 5)) conversation.Close();
        if (!capture && !selfTest && Raylib.IsWindowFocused())
        {
            if (wasTalking)
            {
                conversation.Update(dt);
                if (Raylib.IsKeyPressed(KeyboardKey.Escape)) conversation.Close();
                else if (Raylib.IsKeyPressed(KeyboardKey.E) || Raylib.IsKeyPressed(KeyboardKey.Space)) conversation.Advance();
            }
            else if (explorer.Captured && target != null && Raylib.IsKeyPressed(KeyboardKey.E)) conversation.Start(target);
        }
        if (!capture && !selfTest) audio.Update(Raylib.GetFrameTime(), world, explorer, renderer);
        if (capture) explorer.GoTo(captures[Math.Min(frame / 4, captures.Length - 1)]);
        if (capturePredator && frame / 4 == 3) world.Predator.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Hunting);
        if (capturePredator && frame / 4 == 5)
        { world.Predator.Stage(new(82.1f, -23, 28), Vector3.UnitZ, PredatorState.Patrol); swallowed = 0; }
        if (capturePredator && frame / 4 >= 6)
        { world.Predator.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Patrol); swallowed = 0; }
        if (capturePredator && frame / 4 == 4)
        {
            if (frame % 4 == 0)
            {
                world.Predator.Stage(new(96, -49, 96), Vector3.UnitZ, PredatorState.Hunting);
                explorer.Position = world.Predator.Mouth + Vector3.UnitY * .9f;
                if (!world.Predator.Update(.01f, world, explorer)) throw new InvalidOperationException("Capture swallow failed.");
            }
            swallowed = .85f;
        }
        double? renderTime = captureCycle ? cycleTimes[Math.Min(frame / 8, cycleTimes.Length - 1)]
            : capture ? (captureNight ? 450 : 150) : selfTest ? new double[] { 150, 300, 450, 600 }[testStop % 4] : null;
        Raylib.BeginDrawing(); renderer.Render(world, explorer.Camera, capture ? 12 : null, renderTime);
        if (!capture) DrawHud(world, explorer, renderer, help && conversation.Speaker == null, noticeTime > 0 ? notice : "");
        if (capturePeople && frame / 4 == portraits.Length)
        { if (conversation.Speaker == null) conversation.Start(portraits[0]); conversation.Update(10); }
        if (!capture || capturePeople) conversationUi.Draw(conversation, target);
        if (!capture && !selfTest && (audio.Muted || !audio.Ready))
            Raylib.DrawText(audio.Ready ? "AUDIO MUTED (M)" : "AUDIO UNAVAILABLE", 26, 157, 14, new Color(195, 212, 211, 255));
        conversationUi.DrawSwallowed(swallowed);
        Raylib.EndDrawing(); noticeTime -= dt;
        if (capture && frame % 4 == 3)
        {
            string directory = Path.Combine(SaveStore.DirectoryPath, "Captures"); Directory.CreateDirectory(directory);
            var image = Raylib.LoadImageFromScreen();
            try
            {
                if (!Raylib.ExportImage(image, Path.Combine(directory, $"generated-{(captureWater ? "water-" : captureIdentity ? $"identity-{identityLabel}-" : capturePredator ? "predator-" : capturePeople ? "people-" : captureCycle ? "cycle-" : captureFrameTower ? "frame-tower-" : captureTowerFaces ? "tower-faces-" : captureChaos ? "chaos-" : "")}{(captureNight && !captureCycle ? "night-" : "")}{frame / 4 + 1:00}.png")))
                    throw new IOException("Cannot write capture.");
            }
            finally { Raylib.UnloadImage(image); }
        }
        frame++;
        if (capture && frame >= captures.Length * 4) break;
        if (selfTest && !world.Generating)
        {
            var center = world.GlobalCell(explorer.Position);
            if (world.Sectors.Count(s => Math.Abs(s.X - center.X) <= 2 && Math.Abs(s.Z - center.Z) <= 2) != 25)
                throw new InvalidOperationException("GPU streaming neighborhood is incomplete.");
            if (world.LoadedCount > 49) throw new InvalidOperationException("GPU streaming retention exceeded.");
            if (++stableFrames >= 3)
            {
                stableFrames = 0;
                if (++testStop == testStops.Length) break;
                // Exercise color/depth attachment recreation while streaming the world.
                Raylib.SetWindowSize(testStop % 2 == 0 ? 1280 : 960, testStop % 2 == 0 ? 800 : 640);
            }
        }
    }
    if (!capture && !selfTest) SaveStore.Save(explorer, world);
    if (selfTest)
    {
        if (testStop != testStops.Length) throw new InvalidOperationException("GPU streaming test was interrupted.");
        Directory.CreateDirectory(SaveStore.DirectoryPath);
        File.WriteAllText(Path.Combine(SaveStore.DirectoryPath, "self-test.txt"), "PASS: procedural checks; OpenGL day/night rendering; worker generation, GPU upload, eviction, huge-address boundary rebasing, resizing and return across six camera stops.\n");
    }
}
catch (Exception ex)
{
    Directory.CreateDirectory(SaveStore.DirectoryPath);
    File.WriteAllText(Path.Combine(SaveStore.DirectoryPath, "error.log"), ex.ToString());
    throw;
}
finally { Raylib.CloseWindow(); }

static void DrawHud(World world, Explorer explorer, Renderer renderer, bool help, string notice)
{
    Color ink = new(237, 234, 215, 255), muted = new(195, 212, 211, 255);
    var c = world.GlobalCell(explorer.Position);
    Raylib.DrawRectangle(24, 24, 3, 44, new Color(230, 215, 170, 255));
    Raylib.DrawText("C A E R O  5", 40, 24, 26, ink);
    Raylib.DrawText("THE WANDERING CITADELS", 41, 57, 11, muted);
    Raylib.DrawText($"SEED {world.Seed}   ISLAND {c.X}, {c.Z}   CHAOS {Sector.TierAt(c.X, c.Z)}   LOADED {world.LoadedCount}" + (world.Generating ? "   GENERATING" : ""), 26, 87, 15, muted);
    Raylib.DrawText($"LOCAL {explorer.Position.X:F1}, {explorer.Position.Y:F1}, {explorer.Position.Z:F1}   {(explorer.Flying ? "FLYING" : explorer.Swimming ? "SWIMMING" : "WALKING")}   {Raylib.GetFPS()} FPS", 26, 109, 14, muted);
    if (help)
    {
        int y = Raylib.GetScreenHeight() - 104;
        Raylib.DrawText("WASD Move   Mouse Look   Shift Fast   Ctrl 10x   Space Jump / Rise   C Dive / Descend", 26, y, 15, ink);
        Raylib.DrawText($"F Fly   N Time x{renderer.TimeScale} (1 / 10)   1 First Island   F5 Save   F9 Load   H Help", 26, y + 24, 15, ink);
        Raylib.DrawText("E Talk   Esc Release Mouse   Click Capture   Alt+Enter Fullscreen   M Mute Audio", 26, y + 48, 15, muted);
    }
    if (notice.Length > 0) Raylib.DrawText(notice, 26, 138, 16, ink);
}
