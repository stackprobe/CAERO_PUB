using System.Numerics;

namespace Caero;

internal readonly record struct WaterView(float Depth, float Brightness, float Density, Vector3 FogColor)
{
    public float VisibilityDistance => MathF.Log(10) / Density;
}

internal static class WaterOptics
{
    public static WaterView At(float eyeHeight)
    {
        float depth = Math.Max(0, World.WaterLevel - eyeHeight);
        // Light falls exponentially, while suspended particles obscure nearby objects.
        float brightness = MathF.Exp(-.18f * depth);
        float density = .045f + .016f * depth;
        return new(depth, brightness, density, new Vector3(.012f, .065f, .085f) * brightness);
    }
}
