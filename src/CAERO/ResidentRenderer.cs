using System.Numerics;
using Raylib_cs;

namespace Caero;

// Shared low-poly cloth and anatomy; poses are articulated rather than billboards.
internal sealed class ResidentRenderer : IDisposable
{
    private Model skirt = Cloth(.33f, .20f, true), torso = Cloth(.21f, .28f), sleeve = Cloth(.115f, .145f);
    private Model ball = Raylib.LoadModelFromMesh(Raylib.GenMeshSphere(1, 10, 12));
    private Model hair = Hair();
    private Matrix4x4 root;
    private Shader shader;
    private Color skin;
    private static readonly Color Robe = new(236, 234, 221, 255);
    private static Model Cloth(float bottom, float top, bool dusty = false)
    {
        var g = new Geometry(); const int sides = 16;
        Vector3 P(int i, float y)
        {
            float a = i * MathF.Tau / sides;
            float radius = bottom + (top - bottom) * y;
            radius *= i % 2 == 0 ? 1.035f : .965f;
            return new(MathF.Sin(a) * radius, y, MathF.Cos(a) * radius * .8f);
        }
        for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < sides; i++)
            {
                float lo = ring / 3f, hi = (ring + 1) / 3f;
                var tint = dusty && ring == 0 ? new Color(222, 219, 208, 255) : Color.White;
                g.Quad(P(i, lo), P(i + 1, lo), P(i + 1, hi), P(i, hi), tint);
            }
        for (int i = 0; i < sides; i++)
            g.Triangle(new(0, 1, 0), P(i, 1), P(i + 1, 1), Color.White);
        return g.UploadTiled();
    }
    private static Model Hair()
    {
        var g = new Geometry(); const int sides = 16, rings = 5;
        Vector3 P(int i, int r)
        {
            float a = i * MathF.Tau / sides;
            float extent = 1.5f - .55f * MathF.Cos(a);
            float p = .001f + extent * r / rings;
            return new(MathF.Sin(a) * MathF.Sin(p), MathF.Cos(p), MathF.Cos(a) * MathF.Sin(p));
        }
        for (int r = 0; r < rings; r++)
            for (int i = 0; i < sides; i++)
                g.Quad(P(i, r), P(i, r + 1), P(i + 1, r + 1), P(i + 1, r), Color.White);
        return g.UploadTiled();
    }
    private void Part(ref Model model, Matrix4x4 transform, Color color)
    {
        // raylib's native Matrix stores translation in the fourth column.
        model.Transform = Matrix4x4.Transpose(transform * root);
        unsafe { model.Materials[0].Shader = shader; }
        Raylib.DrawModel(model, Vector3.Zero, 1, color);
    }
    private void Ball(Vector3 p, Vector3 size, Color color) => Part(ref ball, Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(p), color);
    private void Link(ref Model model, Vector3 a, Vector3 b, float width, Color color)
    {
        Vector3 delta = b - a; float length = delta.Length();
        Vector3 axis = Vector3.Cross(Vector3.UnitY, delta / length);
        Quaternion rotation = axis.LengthSquared() < .000001f ? Quaternion.Identity
            : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(delta.Y / length, -1, 1)));
        Part(ref model, Matrix4x4.CreateScale(width, length, width) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(a), color);
    }
    public void Draw(Resident person, Shader worldShader, float? fixedTime = null)
    {
        shader = worldShader;
        float t = fixedTime ?? person.Animation;
        float breath = MathF.Sin(t * 1.15f + person.Phase) * .008f;
        float stride = person.Pose == ResidentPose.Walking && !person.Talking ? MathF.Sin(t * 2.7f) * .12f : 0;
        root = Matrix4x4.CreateScale(person.Scale) * Matrix4x4.CreateRotationY(person.Yaw) * Matrix4x4.CreateTranslation(person.Position);
        if (person.Pose == ResidentPose.Collapsed)
            root = Matrix4x4.CreateRotationZ(-MathF.PI / 2) * Matrix4x4.CreateTranslation(-.75f, .33f, 0) * root;
        skin = (person.Profile % 3) switch { 0 => new(166, 143, 123, 255), 1 => new(198, 174, 151, 255), _ => new(183, 157, 134, 255) };
        bool crouch = person.Pose == ResidentPose.Crouching;
        Vector3 pelvis = new(0, crouch ? .29f : .78f, 0);
        Vector3 shoulders = new(0, (crouch ? .69f : 1.25f) + breath, crouch ? .22f : .12f);
        // Long, unadorned, opaque white cloth with a dusty hem and modest sleeves.
        Vector3 hem = new(0, .065f, crouch ? .18f : 0);
        Link(ref skirt, hem, pelvis, crouch ? 1.15f : person.Female ? 1.03f : 1, Robe);
        Link(ref torso, pelvis, shoulders, person.Female ? .9f : 1, Robe);
        Ball(pelvis + new Vector3(0, .025f, 0), new(.21f, .015f, .16f), new(184, 179, 160, 255));
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 foot = new(side * .13f, .045f, (crouch ? .35f : stride * side) + .06f);
            Ball(foot, new(.085f, .045f, .16f), skin);
            Vector3 shoulder = shoulders + new Vector3(side * .245f, -.045f, 0);
            Vector3 elbow = crouch ? new(side * .24f, .42f, .43f) : shoulder + new Vector3(side * .035f, -.27f, .08f - side * stride);
            Vector3 hand = crouch ? new(side * .15f, .67f, .51f) : elbow + new Vector3(-side * .025f, -.24f, .04f);
            Link(ref sleeve, elbow, shoulder, .95f, Robe);
            Link(ref sleeve, hand, elbow, .7f, Robe);
            Ball(hand, new(.055f, .085f, .045f), skin);
        }
        Vector3 head = crouch ? new(0, .81f + breath, .43f) : new(0, 1.45f + breath, .20f);
        if (person.Pose == ResidentPose.Collapsed) head.X += .14f;
        float tilt = crouch ? .65f : person.Age == ResidentAge.Elder ? .5f : .33f;
        var headRotation = Matrix4x4.CreateRotationX(tilt);
        Vector3 H(Vector3 p) => Vector3.Transform(p, headRotation) + head;
        Link(ref sleeve, shoulders, head - new Vector3(0, .13f, 0), .45f, Robe);
        Part(ref ball, Matrix4x4.CreateScale(.17f, .218f, .168f) * headRotation * Matrix4x4.CreateTranslation(head), skin);
        Color hairColor = person.Age == ResidentAge.Elder ? new(166, 163, 153, 255) : new(66, 59, 51, 255);
        Part(ref hair, Matrix4x4.CreateScale(.179f, .227f, .177f) * headRotation * Matrix4x4.CreateTranslation(head), hairColor);
        if (person.Female)
            Ball(H(new(0, -.09f, -.13f)), new(.15f, person.Age == ResidentAge.Child ? .14f : .22f, .07f), hairColor);
        foreach (float side in new[] { -1f, 1f })
        {
            Ball(H(new(side * .17f, -.01f, 0)), new(.025f, .05f, .03f), skin);
            Ball(H(new(side * .064f, -.023f, .151f)), new(.038f, .012f, .009f), new(140, 119, 104, 255));
            Ball(H(new(side * .064f, -.012f, .155f)), new(.032f, .004f, .009f), new(79, 69, 61, 255));
        }
        Ball(H(new(0, -.045f, .172f)), new(.025f, .04f, .03f), skin);
        Ball(H(new(0, -.11f, .138f)), new(.037f, .008f, .009f), new(112, 91, 80, 255));
    }
    public void Dispose()
    { Raylib.UnloadModel(skirt); Raylib.UnloadModel(torso); Raylib.UnloadModel(sleeve); Raylib.UnloadModel(ball); Raylib.UnloadModel(hair); }
}
