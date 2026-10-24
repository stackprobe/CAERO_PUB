using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class CourtyardEscapeChecks
{
    internal static void Run(bool capture = false)
    {
        if (capture) WindowHost.Open();
        try
        {
            // The courtyard reported in the 2026-10-24 save/screenshot.
            // Never read or modify the player's current save or abilities.
            using var world = new World(upload: capture, seed: 1701, originX: 30, originZ: -241);
            var sector = world.Sectors.Single(s => s.X == 30 && s.Z == -241);
            if (sector.CourtyardOpenings.Count != 1)
                throw new InvalidOperationException("Expected the reported roofless courtyard on the saved island.");
            Vector3 opening = sector.Transform(sector.CourtyardOpenings.Single());
            foreach (float dt in new[] { .01f, .05f, .1f })
            {
                Vector3 start = opening + Vector3.UnitZ * 2.2f;
                var player = new Explorer { Position = start + Vector3.UnitY * Explorer.EyeHeight };
                // Settle on the floor, then walk into the opening without jumping.
                for (int frame = 0; frame < 10; frame++)
                    player.Advance(dt, world, Vector3.Zero, false, false, false, false);
                if (!player.Grounded || Math.Abs(player.Position.Y - start.Y - Explorer.EyeHeight) > .03f)
                    throw new InvalidOperationException("Courtyard test must start on the surviving floor.");
                for (int frame = 0; frame < (int)(1 / dt); frame++)
                {
                    Vector3 delta = opening - player.Position; delta.Y = 0;
                    Vector3 move = delta.Length() > .02f ? Vector3.Normalize(delta) * Math.Min(1, delta.Length() / (5 * dt)) : Vector3.Zero;
                    player.Advance(dt, world, move, false, false, false, false);
                }
                for (int frame = 0; frame < (int)(3 / dt); frame++)
                    player.Advance(dt, world, Vector3.Zero, false, false, false, false);
                if (world.Progress.CanFly || player.Flying || player.Position.Y >= opening.Y - 3 || world.Blocked(player.Position))
                    throw new InvalidOperationException($"Courtyard drop-through escape failed at dt={dt}: {player.Position}");
                Vector3 below = player.Position;
                for (int frame = 0; frame < (int)(6 / dt); frame++)
                    player.Advance(dt, world, Vector3.UnitZ, false, false, false, false);
                if (player.Position.Z < below.Z + 20 || world.Blocked(player.Position))
                    throw new InvalidOperationException("Player must remain free to move after dropping out of the courtyard.");
            }
            if (capture)
            {
                using var renderer = new Renderer(world, 12345);
                var player = new Explorer();
                string folder = Path.Combine(SaveStore.DirectoryPath, "Captures"); Directory.CreateDirectory(folder);
                Vector3 savedEye = new(13.992029f, 133.75f, 31.77715f);
                player.Restore(new(savedEye.X, savedEye.Y, savedEye.Z, 2.6906035f, -.8652002f, false));
                Viewpoint[] views = [
                    new("saved-view", savedEye, savedEye + player.Forward),
                    new("opening", opening + new Vector3(0, 14, 6), opening),
                    new("floor", opening + new Vector3(0, Explorer.EyeHeight, 3), opening)
                ];
                foreach (var view in views)
                {
                    player.GoTo(view);
                    for (int frame = 0; frame < 4; frame++)
                    {
                        Raylib.BeginDrawing(); renderer.Render(world, player.Camera, 0, 150); Raylib.EndDrawing();
                    }
                    var image = Raylib.LoadImageFromScreen();
                    try { if (!Raylib.ExportImage(image, Path.Combine(folder, $"courtyard-{view.Name}.png"))) throw new IOException("Courtyard capture failed."); }
                    finally { Raylib.UnloadImage(image); }
                }
            }
            Console.WriteLine("COURTYARD: saved-island floor-to-opening escape passed without jumping, flight or upgrades at three frame steps.");
        }
        finally { if (capture) Raylib.CloseWindow(); }
    }
}
