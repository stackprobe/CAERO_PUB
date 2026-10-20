using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed unsafe class PredatorRenderer : IDisposable
{
    private Model body = Body();
    private Model ball = Raylib.LoadModelFromMesh(Raylib.GenMeshSphere(1, 14, 18));
    private Model tooth = Tooth();
    private static readonly Color Hide = new(48, 63, 59, 255);
    private static Model Body()
    {
        var g = new Geometry(); const int sides = 20;
        (float Z, float W, float H)[] rings = [(-11, .25f, .45f), (-8, 1, 1.25f), (-4, 2.3f, 2),
            (0, 3.2f, 2.65f), (4, 3.5f, 3), (7.8f, 2.6f, 2.1f)];
        Vector3 P(int r, int i)
        {
            float angle = i * MathF.Tau / sides;
            return new(MathF.Sin(angle) * rings[r].W, MathF.Cos(angle) * rings[r].H, rings[r].Z);
        }
        for (int r = 0; r + 1 < rings.Length; r++)
            for (int i = 0; i < sides; i++)
            {
                float belly = .5f - .5f * MathF.Cos(i * MathF.Tau / sides);
                Color color = new((byte)(40 + belly * 18), (byte)(56 + belly * 16), (byte)(54 + belly * 12), (byte)255);
                g.Quad(P(r, i), P(r + 1, i), P(r + 1, i + 1), P(r, i + 1), color);
            }
        void Fin(Vector3 a, Vector3 b, Vector3 c)
        { g.Triangle(a, b, c, Hide); g.Triangle(c, b, a, Hide); }
        Fin(new(0, 2, 3), new(0, 5.3f, -3), new(0, 1.2f, -8));
        Fin(new(-2.5f, -.5f, 3), new(-5.7f, -1.6f, -2), new(-2, -.9f, -4));
        Fin(new(2.5f, -.5f, 3), new(5.7f, -1.6f, -2), new(2, -.9f, -4));
        Fin(new(0, 0, -10), new(0, 3.3f, -15.7f), new(0, 0, -14));
        Fin(new(0, 0, -10), new(0, -3.3f, -15.7f), new(0, 0, -14));
        return g.UploadTiled();
    }
    private static Model Tooth()
    {
        var g = new Geometry(); g.Cylinder(Vector3.Zero, .18f, .005f, .85f, Color.White, 7);
        return g.UploadTiled();
    }
    public void Draw(SeaPredator fish, Shader shader, float? artTime)
    {
        float time = artTime ?? fish.Time;
        // Keep the dorsal fin up even while reversing. A shortest-arc quaternion
        // from +Z can roll the fish upside down when it faces backward and upward.
        Matrix4x4 root = Matrix4x4.CreateRotationX(-MathF.Asin(Math.Clamp(fish.Forward.Y, -1, 1)))
            * Matrix4x4.CreateRotationY(MathF.Atan2(fish.Forward.X, fish.Forward.Z))
            * Matrix4x4.CreateTranslation(fish.Position);
        void Set(string name, float value) => Raylib.SetShaderValue(shader, Raylib.GetShaderLocation(shader, name), &value, ShaderUniformDataType.Float);
        void Part(ref Model model, Vector3 position, Vector3 scale, Color color, float tilt = 0)
        {
            model.Transform = Matrix4x4.Transpose(Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(tilt) * Matrix4x4.CreateTranslation(position) * root);
            model.Materials[0].Shader = shader;
            Raylib.DrawModel(model, Vector3.Zero, 1, color);
        }
        Set("fishPass", 1); Set("fishTime", time); Set("fishSpeed", fish.State == PredatorState.Hunting ? 3 : 1);
        Set("fishWarp", 1); Part(ref body, Vector3.Zero, Vector3.One, Color.White); Set("fishWarp", 0);
        float gape = fish.Gape;
        Part(ref ball, new(0, -gape * .6f, 8.25f), new(2.55f, .5f + gape * 1.65f, .8f), new(6, 10, 10, 255));
        Part(ref ball, new(0, 1.3f, 7.4f), new(2.8f, 1.25f, 1.8f), Hide);
        Part(ref ball, new(0, -1.1f - gape * 1.8f, 7.4f), new(2.65f, .9f, 1.8f), Hide);
        foreach (float x in new[] { -2f, -1, 0, 1, 2 })
        {
            var ivory = new Color(116, 128, 114, 255);
            Part(ref tooth, new(x, .35f, 8.85f), new(.7f, .8f, .7f), ivory, MathF.PI);
            Part(ref tooth, new(x, -1.1f - gape * 1.8f, 8.85f), new(.7f, .7f, .7f), ivory);
        }
        foreach (float sign in new[] { -1f, 1f })
        {
            Part(ref ball, new(sign * 2.8f, 1.55f, 8.1f), new(.5f, .4f, .42f), new(18, 24, 22, 255));
            Set("fishEye", 1);
            Part(ref ball, new(sign * 2.85f, 1.55f, 8.4f), new(.34f, .29f, .32f), new(111, 132, 73, 255));
            Set("fishEye", 0);
        }
        Set("fishPass", 0);
    }
    public void Dispose() { Raylib.UnloadModel(body); Raylib.UnloadModel(ball); Raylib.UnloadModel(tooth); }
}
