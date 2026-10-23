using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;

namespace Caero;

internal static class WarpRenderChecks
{
    private sealed record Frame(double Seconds, float Strength, bool Arrived, int Loaded, double UpdateMs, double FrameMs);
    private sealed class Captures(bool enabled) : IDisposable
    {
        private readonly Dictionary<string, int> observed = [];
        private readonly List<(Image Image, string Name)> images = [];
        internal void Observe(string? name)
        {
            if (!enabled || name == null || name == "05-restored") return;
            int count = observed.GetValueOrDefault(name) + 1; observed[name] = count;
            // Let the swapped front/back buffers settle before taking each stage.
            if (count == 3) images.Add((Raylib.LoadImageFromScreen(), name));
        }
        internal void Restored() { if (enabled) images.Add((Raylib.LoadImageFromScreen(), "05-restored")); }
        internal void Save(string directory)
        {
            foreach (var (image, name) in images)
                if (!Raylib.ExportImage(image, Path.Combine(directory, "warp-transition-" + name + ".png")))
                    throw new IOException("Cannot save warp transition capture.");
        }
        public void Dispose() { foreach (var (image, _) in images) Raylib.UnloadImage(image); }
    }
    internal static void Run(bool capture)
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        WindowHost.Open();
        try
        {
            using var world = new World(originX: -43, originZ: -33);
            using var renderer = new Renderer(world, 12345);
            var source = world.Sectors.Single(s => s.X == -43 && s.Z == -33);
            var machine = new WarpTarget(source, source.WarpTerminals[0]);
            Vector3 facing = source.Transform(machine.Terminal.LocalFacing) - source.Offset;
            var player = new Explorer();
            player.GoTo(new("WARP SOURCE", machine.Point + facing * 2 + Vector3.UnitY * .6f, machine.Point));
            for (int i = 0; i < 15; i++)
            {
                world.Update(player.Position);
                Raylib.BeginDrawing(); renderer.Render(world, player.Camera, 12, 150); Raylib.EndDrawing();
            }
            var activation = Stopwatch.StartNew();
            if (!WarpTravel.Travel(world, player, machine)) throw new InvalidOperationException("Cannot activate warp.");
            activation.Stop();
            if (world.OriginX != source.X || world.OriginZ != source.Z) throw new InvalidOperationException("Activation synchronously relocated the world.");
            var frames = new List<Frame>();
            using var snapshots = new Captures(capture);
            string directory = Path.Combine(SaveStore.DirectoryPath, "Captures");
            Directory.CreateDirectory(directory);
            var elapsed = Stopwatch.StartNew();
            do
            {
                if (Raylib.WindowShouldClose()) throw new InvalidOperationException("Warp render check interrupted.");
                if (elapsed.Elapsed.TotalSeconds > 30) throw new TimeoutException("Warp render check timed out.");
                var frame = Stopwatch.StartNew();
                float dt = Math.Clamp(Raylib.GetFrameTime(), .001f, .05f);
                int installed = world.InstalledSectorCount;
                world.AdvanceWarp(dt, player); world.Update(player.Position);
                if (world.InstalledSectorCount - installed > 1) throw new InvalidOperationException("More than one island uploaded in one frame.");
                if (world.WarpFalling || !world.WarpInProgress)
                    player.Advance(dt, world, Vector3.Zero, false, false, false, false);
                double updateMs = frame.Elapsed.TotalMilliseconds;
                Raylib.BeginDrawing(); renderer.Render(world, player.Camera, 12, 150); Raylib.EndDrawing();
                bool arrived = world.OriginX != source.X || world.OriginZ != source.Z;
                frames.Add(new(elapsed.Elapsed.TotalSeconds, world.WarpStrength, arrived, world.LoadedCount, updateMs, frame.Elapsed.TotalMilliseconds));
                string? shot = !world.WarpInProgress ? "05-restored" : arrived
                    ? world.WarpStrength < .6f ? "04-recovering" : "03-arrival"
                    : world.WarpStrength > .95f ? "02-departure-peak" : world.WarpStrength > .4f ? "01-departing" : null;
                snapshots.Observe(shot);
            } while (world.WarpInProgress);
            elapsed.Stop();
            if (capture)
            {
                for (int i = 0; i < 3; i++)
                { Raylib.BeginDrawing(); renderer.Render(world, player.Camera, 12, 150); Raylib.EndDrawing(); }
                snapshots.Restored();
                // Compress screenshots after the animation, outside frame measurements.
                snapshots.Save(directory);
            }
            if (!player.Grounded || world.Blocked(player.Position)) throw new InvalidOperationException("Warp arrival did not land safely.");
            double maxUpdate = frames.Max(f => f.UpdateMs), maxFrame = frames.Max(f => f.FrameMs);
            var report = new { ActivationMs = activation.Elapsed.TotalMilliseconds, DurationSeconds = elapsed.Elapsed.TotalSeconds,
                Frames = frames.Count, MaxUpdateMs = maxUpdate, MaxFrameMs = maxFrame, LoadedAtFinish = world.LoadedCount,
                ArrivedFrames = frames.Count(f => f.Arrived), Samples = frames };
            File.WriteAllText(Path.Combine(SaveStore.DirectoryPath, capture ? "warp-transition-capture.json" : "warp-transition-render.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"WARP RENDER: activation {report.ActivationMs:F2}ms, {report.DurationSeconds:F2}s / {frames.Count} frames, max update {maxUpdate:F2}ms, max frame {maxFrame:F2}ms, {world.LoadedCount} islands, grounded arrival.");
            if (!capture && (report.ActivationMs > 150 || maxUpdate > 250 || maxFrame > 500))
                throw new InvalidOperationException("Warp still blocks the interactive render loop; see warp-transition-render.json.");
        }
        finally { Raylib.CloseWindow(); }
    }
}
