using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed class AltitudeBoundary
{
    internal const float HeightLimit = 500, GlitchDuration = 3;
    internal bool Active { get; private set; }
    internal float Elapsed { get; private set; }
    internal float Strength
    {
        get
        {
            if (!Active) return 0;
            float t = Math.Clamp(Elapsed / GlitchDuration, 0, 1);
            return .08f + .92f * t * t * (3 - 2 * t);
        }
    }
    internal bool Update(float height, float dt)
    {
        if (!Active && height <= HeightLimit) return false;
        Active = true;
        Elapsed += Math.Max(0, dt);
        return Elapsed >= GlitchDuration;
    }
    internal Camera3D Distort(Camera3D camera)
    {
        if (!Active) return camera;
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        // Change only the rendered view, never the player's saved direction or position.
        camera.Target += (right * MathF.Sin(Elapsed * 41) * .035f
            + camera.Up * MathF.Sin(Elapsed * 67) * .025f) * Strength;
        camera.Up = Vector3.Normalize(camera.Up + right * (.04f * Strength * MathF.Sin(Elapsed * 23)));
        return camera;
    }
    internal void Reset() { Active = false; Elapsed = 0; }
}
