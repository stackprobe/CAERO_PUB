using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed unsafe class Renderer : IDisposable
{
    private readonly Shader worldShader, postShader, skyShader, blurShader;
    private readonly Texture2D waterNoise, surfaceDetail, inscriptionScript;
    private readonly Model seabed, ocean;
    private readonly SceneVisibility visibility = new();
    private readonly ResidentRenderer people = new();
    private readonly PredatorRenderer predator = new();
    private readonly List<SceneChunk> visibleChunks = [];
    public int DrawnChunks { get; private set; }
    public int DrawnTriangles { get; private set; }
    private RenderTexture2D scene, blurHorizontal, blurVertical, presentation;
    private readonly DayNightCycle cycle;
    private readonly LanternSchedule lanterns;
    public bool IsNight => cycle.State.IsNight;
    public SkyState Sky => cycle.State;
    public float LanternBrightness(NightLight light) => lanterns.Brightness(light, cycle.Seconds);
    public const float NightDrawDistance = 60;
    public int TimeScale => cycle.TimeScale;
    public void ToggleTimeScale() => cycle.ToggleSpeed();
    public void StartAtNight() => cycle.StartAtNight();
    public void Update(double dt) => cycle.Advance(dt);
    private const float NearClip = .15f;
    private readonly int eyeLocation, timeLocation, resolutionLocation;
    private readonly int lampsLocation, lampMinLocation, lampMaxLocation, lightCountLocation, lampIntensityLocation;
    private readonly Vector4[] lampData = new Vector4[24];
    private readonly float[] lampIntensity = new float[24];
    private readonly Vector3[] lampMin = new Vector3[24], lampMax = new Vector3[24];
    private NightLight[] activeLights = [];
    private (long, long)? phaseOrigin;
    public Renderer(World world, int? lampSeed = null)
    {
        cycle = world.Clock;
        lanterns = new(lampSeed ?? Random.Shared.Next());
        string ShaderPath(string name) => Path.Combine(AppContext.BaseDirectory, "Shaders", name);
        worldShader = Raylib.LoadShader(ShaderPath("world.vs"), ShaderPath("world.fs"));
        postShader = Raylib.LoadShader(null, ShaderPath("post.fs"));
        skyShader = Raylib.LoadShader(null, ShaderPath("sky.fs"));
        blurShader = Raylib.LoadShader(null, ShaderPath("blur.fs"));
        if (!Raylib.IsShaderValid(worldShader) || !Raylib.IsShaderValid(postShader) || !Raylib.IsShaderValid(skyShader) || !Raylib.IsShaderValid(blurShader))
            throw new InvalidOperationException("Shader initialization failed. Check OpenGL 3.3 support.");
        Set(worldShader, "waterLevel", World.WaterLevel);
        Set(skyShader, "waterLevel", World.WaterLevel);
        Set(skyShader, "seaFloorLevel", World.SeaFloor);
        Set(skyShader, "nearClip", NearClip);
        eyeLocation = Raylib.GetShaderLocation(worldShader, "eye");
        timeLocation = Raylib.GetShaderLocation(worldShader, "time");
        lampsLocation = Raylib.GetShaderLocation(worldShader, "lamps[0]");
        lampMinLocation = Raylib.GetShaderLocation(worldShader, "lampMin[0]");
        lampMaxLocation = Raylib.GetShaderLocation(worldShader, "lampMax[0]");
        lightCountLocation = Raylib.GetShaderLocation(worldShader, "lightCount");
        lampIntensityLocation = Raylib.GetShaderLocation(worldShader, "lampIntensity[0]");
        worldShader.Locs[(int)ShaderLocationIndex.MapRoughness] = Raylib.GetShaderLocation(worldShader, "waterNoise");
        worldShader.Locs[(int)ShaderLocationIndex.MapNormal] = Raylib.GetShaderLocation(worldShader, "surfaceDetail");
        resolutionLocation = Raylib.GetShaderLocation(postShader, "resolution");
        waterNoise = SurfaceTextures.WaterNoise();
        surfaceDetail = SurfaceTextures.SurfaceDetail();
        inscriptionScript = InscriptionSurface.CreateTexture();
        var water = new Geometry();
        water.Quad(new(-600, 0, 600), new(600, 0, 600), new(600, 0, -600), new(-600, 0, -600), new(61, 110, 118, 100));
        ocean = water.UploadTiled();
        Bind(ocean);
        ocean.Materials[0].Shader = worldShader;
        seabed = Raylib.LoadModelFromMesh(Raylib.GenMeshPlane(1200, 1200, 1, 1));
        seabed.Materials[0].Shader = worldShader;
        var size = WindowHost.RenderSize;
        scene = CreateScene(size.Width, size.Height, true);
        CreateBlurTargets(scene.Texture.Width, scene.Texture.Height);
    }
    private void CreateBlurTargets(int width, int height)
    {
        blurHorizontal = CreateScene(Math.Max(1, (width + 1) / 2), Math.Max(1, (height + 1) / 2));
        blurVertical = CreateScene(blurHorizontal.Texture.Width, blurHorizontal.Texture.Height);
    }
    private void BlurPass(Texture2D source, RenderTexture2D target, Vector2 direction)
    {
        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(blurShader);
        Raylib.SetShaderValue(blurShader, Raylib.GetShaderLocation(blurShader, "stepUv"), &direction, ShaderUniformDataType.Vec2);
        Raylib.DrawTexturePro(source, new(0, 0, source.Width, -source.Height),
            new(0, 0, target.Texture.Width, target.Texture.Height), Vector2.Zero, 0, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }
    private static RenderTexture2D CreateScene(int width, int height, bool sampleDepth = false)
    {
        // Only the main scene samples depth, once per pixel to classify water/air.
        var target = sampleDepth ? new RenderTexture2D {
            Id = Rlgl.LoadFramebuffer(),
            Texture = new Texture2D { Id = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR8G8B8A8, 1),
                Width = width, Height = height, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 },
            Depth = new Texture2D { Id = Rlgl.LoadTextureDepth(width, height, false), Width = width, Height = height, Mipmaps = 1 }
        } : Raylib.LoadRenderTexture(width, height);
        if (sampleDepth)
        {
            Rlgl.FramebufferAttach(target.Id, target.Texture.Id, FramebufferAttachType.ColorChannel0, FramebufferAttachTextureType.Texture2D, 0);
            Rlgl.FramebufferAttach(target.Id, target.Depth.Id, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);
            Raylib.SetTextureFilter(target.Depth, TextureFilter.Point);
            Raylib.SetTextureWrap(target.Depth, TextureWrap.Clamp);
        }
        if (!Rlgl.FramebufferComplete(target.Id))
        {
            Raylib.UnloadRenderTexture(target);
            throw new InvalidOperationException("Scene framebuffer is incomplete.");
        }
        Raylib.SetTextureFilter(target.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(target.Texture, TextureWrap.Clamp);
        return target;
    }
    private void Bind(Model model)
    {
        model.Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = inscriptionScript;
        model.Materials[0].Maps[(int)MaterialMapIndex.Roughness].Texture = waterNoise;
        model.Materials[0].Maps[(int)MaterialMapIndex.Normal].Texture = surfaceDetail;
    }
    private static void Set(Shader shader, string name, Vector3 value)
        => Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, name), &value, ShaderUniformDataType.Vec3);
    private static void Set(Shader shader, string name, float value)
        => Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, name), &value, ShaderUniformDataType.Float);
    private static void Set(Shader shader, string name, Vector4 value)
        => Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, name), &value, ShaderUniformDataType.Vec4);
    private static void Draw(Model model, Shader shader, Vector3 offset)
    {
        model.Materials[0].Shader = shader;
        Raylib.DrawModel(model, offset, 1, Color.White);
    }
    internal static double WavePhase(long cell, double step)
    {
        ulong remaining = cell < 0 ? (ulong)(-(Int128)cell) : (ulong)cell;
        double result = 0;
        step = Math.IEEERemainder(step, Math.Tau);
        while (remaining != 0)
        {
            if ((remaining & 1) != 0) result = Math.IEEERemainder(result + step, Math.Tau);
            step = Math.IEEERemainder(step * 2, Math.Tau); remaining >>= 1;
        }
        return cell < 0 ? -result : result;
    }
    public void Render(World world, Camera3D camera, float? artTime = null, double? cycleTime = null,
        float? warpStrength = null, float? warpTime = null, float boundaryStrength = 0)
    {
        int screenWidth = Raylib.GetScreenWidth(), screenHeight = Raylib.GetScreenHeight();
        var (width, height) = WindowHost.RenderSize;
        if (screenWidth < 1 || screenHeight < 1 || width < 1 || height < 1) return;
        if (scene.Texture.Width != width || scene.Texture.Height != height)
        {
            Raylib.UnloadRenderTexture(scene); Raylib.UnloadRenderTexture(blurHorizontal); Raylib.UnloadRenderTexture(blurVertical);
            scene = CreateScene(width, height, true); CreateBlurTargets(width, height);
            if (presentation.Id != 0) { Raylib.UnloadRenderTexture(presentation); presentation = default; }
        }
        var eye = camera.Position; float time = artTime ?? (float)Raylib.GetTime();
        if (phaseOrigin != (world.OriginX, world.OriginZ))
        {
            Set(worldShader, "waterPhase", new Vector3((float)Math.IEEERemainder(WavePhase(world.OriginX, 192 * .31) + WavePhase(world.OriginZ, 192 * .17), Math.Tau),
                (float)WavePhase(world.OriginZ, 192 * .26), (float)WavePhase(world.OriginX, 192 * .09)));
            phaseOrigin = (world.OriginX, world.OriginZ);
        }
        var sky = cycleTime.HasValue ? DayNightCycle.At(cycleTime.Value) : cycle.State;
        UpdateLights(world, eye, sky.Seconds);
        var exit = world.Sectors.SelectMany(s => s.SpecialObjects).FirstOrDefault(o => o.Definition.Kind == SpecialKind.Exit);
        Set(worldShader, "exitLight", exit == null ? Vector4.Zero : new Vector4(exit.Foot + Vector3.UnitY * 1.45f, 9));
        Set(worldShader, "exitPulse", ExitRelic.Brightness(time));
        float drawDistance = SceneVisibility.DrawDistance + (NightDrawDistance - SceneVisibility.DrawDistance) * sky.Night;
        Set(worldShader, "drawDistance", drawDistance);
        foreach (var shader in new[] { worldShader, skyShader })
        {
            Set(shader, "night", sky.Night);
            Set(shader, "sunDirection", sky.Sun); Set(shader, "moonDirection", sky.Moon);
            Set(shader, "sunOpacity", sky.SunOpacity); Set(shader, "moonOpacity", sky.MoonOpacity);
        }
        var water = WaterOptics.At(eye.Y);
        Vector3 waterFog = water.FogColor * (1 - .975f * sky.Night);
        Set(worldShader, "waterBrightness", water.Brightness);
        Set(worldShader, "waterDensity", water.Density * (1 + .8f * sky.Night));
        Set(worldShader, "waterFogColor", waterFog);
        Set(skyShader, "waterFogColor", waterFog);
        Raylib.SetShaderValue(worldShader, eyeLocation, &eye, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(worldShader, timeLocation, &time, ShaderUniformDataType.Float);
        Raylib.BeginTextureMode(scene);
        Raylib.ClearBackground(Color.Black);
        Vector2 skyResolution = new(width, height);
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, forward);
        float tanHalfFov = MathF.Tan(camera.FovY * MathF.PI / 360);
        Set(skyShader, "forward", forward); Set(skyShader, "right", right);
        Set(skyShader, "up", up); Set(skyShader, "eye", eye);
        Raylib.SetShaderValue(skyShader, Raylib.GetShaderLocation(skyShader, "resolution"), &skyResolution, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(skyShader, Raylib.GetShaderLocation(skyShader, "tanHalfFov"), &tanHalfFov, ShaderUniformDataType.Float);
        Raylib.BeginShaderMode(skyShader);
        Raylib.DrawRectangle(0, 0, width, height, Color.White);
        Raylib.EndShaderMode();
        visibility.Update(camera, width / (float)height, drawDistance);
        visibleChunks.Clear();
        foreach (var chunk in world.Chunks)
            if (visibility.Visible(chunk.Min, chunk.Max))
            { chunk.SortDistanceSquared = chunk.DistanceSquared(eye); visibleChunks.Add(chunk); }
        visibleChunks.Sort(static (a, b) => a.SortDistanceSquared.CompareTo(b.SortDistanceSquared));
        DrawnChunks = visibleChunks.Count; DrawnTriangles = 0;
        Rlgl.SetClipPlanes(NearClip, drawDistance);
        Raylib.BeginMode3D(camera);
        // The near plane can cut through the surface before the eye submerges.
        if (eye.Y < World.WaterLevel + .5f)
        {
            int seabedPass = 1;
            int location = Raylib.GetShaderLocation(worldShader, "seabedPass");
            Raylib.SetShaderValue(worldShader, location, &seabedPass, ShaderUniformDataType.Int);
            Raylib.DrawModel(seabed, new(eye.X, World.SeaFloor, eye.Z), 1, Color.White);
            seabedPass = 0;
            Raylib.SetShaderValue(worldShader, location, &seabedPass, ShaderUniformDataType.Int);
        }
        foreach (var chunk in visibleChunks)
        {
            Set(worldShader, "smoothMaterial", chunk.SmoothMaterial ? 1 : 0);
            Set(worldShader, "exitRelicPass", chunk.ExitRelic ? 1 : 0);
            Set(worldShader, "inscriptionPass", chunk.Inscription ? 1 : 0);
            Set(worldShader, "primeNumber", chunk.PrimeNumber ? 1 : 0);
            Set(worldShader, "destinationPrimes", chunk.DestinationPrimes);
            Set(worldShader, "wallFinish", chunk.WallFinish);
            Set(worldShader, "wallStrength", chunk.WallStrength);
            Bind(chunk.Detailed);
            Set(worldShader, "materialOffset", chunk.MaterialOffset);
            Draw(chunk.Detailed, worldShader, chunk.Offset);
            DrawnTriangles += chunk.DetailedTriangles;
        }
        Set(worldShader, "wallFinish", Vector3.Zero);
        Set(worldShader, "smoothMaterial", 0);
        Set(worldShader, "exitRelicPass", 0);
        Set(worldShader, "inscriptionPass", 0);
        // A camera-centered ocean covers the streamed area without owning physics or sectors.
        Set(worldShader, "characterPass", 1);
        foreach (var person in world.People)
            if (Vector3.DistanceSquared(person.Position, eye) < 55 * 55 && visibility.Visible(person.Position - Vector3.One * 2, person.Position + Vector3.One * 2))
                people.Draw(person, worldShader, artTime);
        Set(worldShader, "characterPass", 0);
        if (world.Predator.Active && eye.Y < World.WaterLevel + .1f
            && visibility.Visible(world.Predator.Position - new Vector3(22), world.Predator.Position + new Vector3(22)))
        {
            // Fish remain opaque and depth tested; alpha is only a post-effect mask.
            // No extra fish pass or mask texture is needed.
            Rlgl.DrawRenderBatchActive();
            Rlgl.DisableColorBlend();
            predator.Draw(world.Predator, worldShader, artTime);
            Rlgl.EnableColorBlend();
        }
        Rlgl.DisableBackfaceCulling();
        Raylib.DrawModel(ocean, new(eye.X, World.WaterLevel, eye.Z), 1, Color.White);
        Rlgl.EnableBackfaceCulling();
        // Panes use an unlit material so the fixtures themselves glow at night.
        for (int i = 0; i < activeLights.Length; i++)
        {
            float strength = lampIntensity[i];
            Raylib.DrawSphereEx(activeLights[i].Position, .24f, 6, 8,
                new Color((byte)(60 + 195 * strength), (byte)(55 + 156 * strength), (byte)(43 + 82 * strength), (byte)255));
        }
        Raylib.EndMode3D(); Raylib.EndTextureMode();
        Vector3 rayHeight = new(forward.Y, right.Y * width / height * tanHalfFov, up.Y * tanHalfFov);
        float nearMinimumY = eye.Y + NearClip * (rayHeight.X - Math.Abs(rayHeight.Y) - Math.Abs(rayHeight.Z));
        bool waterVisible = nearMinimumY < World.WaterLevel + .002f;
        if (waterVisible)
        {
            // Establish the half-resolution grid before filtering contiguous texels.
            BlurPass(scene.Texture, blurVertical, Vector2.Zero);
            BlurPass(blurVertical.Texture, blurHorizontal, new(1f / blurHorizontal.Texture.Width, 0));
            BlurPass(blurHorizontal.Texture, blurVertical, new(0, 1f / blurVertical.Texture.Height));
        }
        Vector2 resolution = new(width, height);
        Raylib.SetShaderValue(postShader, resolutionLocation, &resolution, ShaderUniformDataType.Vec2);
        if (WindowHost.Borderless)
        {
            // Post effects also run at the saved window size; only the final copy is full size.
            if (presentation.Id == 0) presentation = CreateScene(width, height);
            Raylib.BeginTextureMode(presentation);
            Raylib.ClearBackground(Color.Black);
        }
        Raylib.BeginShaderMode(postShader);
        Set(postShader, "warpStrength", warpStrength ?? world.WarpStrength);
        Set(postShader, "warpTime", warpTime ?? world.WarpTime);
        Set(postShader, "boundaryStrength", boundaryStrength);
        Set(postShader, "waterPass", waterVisible ? 1 : 0);
        if (waterVisible)
        {
            Set(postShader, "rayHeight", rayHeight); Set(postShader, "eyeHeight", eye.Y);
            Set(postShader, "nearClip", NearClip); Set(postShader, "farClip", drawDistance);
            Set(postShader, "waterLevel", World.WaterLevel);
            Raylib.SetShaderValueTexture(postShader, Raylib.GetShaderLocation(postShader, "sceneDepth"), scene.Depth);
            Raylib.SetShaderValueTexture(postShader, Raylib.GetShaderLocation(postShader, "blurredScene"), blurVertical.Texture);
        }
        Raylib.DrawTexturePro(scene.Texture, new Rectangle(0, 0, width, -height),
            new Rectangle(0, 0, width, height), Vector2.Zero, 0, Color.White);
        Raylib.EndShaderMode();
        if (WindowHost.Borderless)
        {
            Raylib.EndTextureMode();
            Raylib.DrawTexturePro(presentation.Texture, new(0, 0, width, -height),
                new(0, 0, screenWidth, screenHeight), Vector2.Zero, 0, Color.White);
        }
    }
    private void UpdateLights(World world, Vector3 eye, double seconds)
    {
        activeLights = world.Lights.Where(l => Vector3.DistanceSquared(l.Position, eye) < MathF.Pow(NightDrawDistance + l.Radius, 2))
            .OrderBy(l => Math.Max(0, Vector3.Distance(l.Position, eye) - l.Radius)).Take(lampData.Length).ToArray();
        int count = activeLights.Length;
        for (int i = 0; i < count; i++)
        {
            lampData[i] = new(activeLights[i].Position, activeLights[i].Radius);
            lampMin[i] = activeLights[i].Min; lampMax[i] = activeLights[i].Max;
            lampIntensity[i] = lanterns.Brightness(activeLights[i], seconds);
        }
        Raylib.SetShaderValue(worldShader, lightCountLocation, &count, ShaderUniformDataType.Int);
        if (count == 0) return;
        fixed (Vector4* data = lampData)
            Raylib.SetShaderValueV(worldShader, lampsLocation, data, ShaderUniformDataType.Vec4, count);
        fixed (Vector3* data = lampMin)
            Raylib.SetShaderValueV(worldShader, lampMinLocation, data, ShaderUniformDataType.Vec3, count);
        fixed (Vector3* data = lampMax)
            Raylib.SetShaderValueV(worldShader, lampMaxLocation, data, ShaderUniformDataType.Vec3, count);
        fixed (float* data = lampIntensity)
            Raylib.SetShaderValueV(worldShader, lampIntensityLocation, data, ShaderUniformDataType.Float, count);
    }

    public void Dispose()
    {
        people.Dispose();
        predator.Dispose();
        Raylib.UnloadModel(seabed);
        Raylib.UnloadModel(ocean);
        Raylib.UnloadRenderTexture(scene);
        Raylib.UnloadRenderTexture(blurHorizontal); Raylib.UnloadRenderTexture(blurVertical);
        if (presentation.Id != 0) Raylib.UnloadRenderTexture(presentation);
        Raylib.UnloadTexture(waterNoise);
        Raylib.UnloadTexture(surfaceDetail);
        Raylib.UnloadTexture(inscriptionScript);
        Raylib.UnloadShader(worldShader); Raylib.UnloadShader(postShader);
        Raylib.UnloadShader(skyShader);
        Raylib.UnloadShader(blurShader);
    }
}
