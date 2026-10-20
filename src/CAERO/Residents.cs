using System.Numerics;

namespace Caero;

internal enum ResidentPose { Walking, Crouching, Collapsed }
internal enum ResidentAge { Child, Adult, Elder }
internal sealed class Resident
{
    public required int Id { get; init; }
    public required int Profile { get; init; }
    public required Vector3 Anchor { get; set; }
    public required Vector3 Patrol { get; set; }
    public required ResidentPose Pose { get; init; }
    public required ResidentAge Age { get; init; }
    public required bool Female { get; init; }
    public required float Scale { get; init; }
    public required float Phase { get; init; }
    public Vector3 Position { get; private set; }
    public float Yaw { get; private set; }
    public float Animation { get; private set; }
    public bool Talking { get; set; }
    public string Name => Dialogue.Lines[Profile].Name;
    public Vector3 LookPoint => Position + Vector3.UnitY * Scale * (Pose == ResidentPose.Collapsed ? .3f : Pose == ResidentPose.Crouching ? .8f : 1.4f);
    private float progress;
    private int direction = 1;
    public void Initialize(float yaw)
    { Position = Anchor; Yaw = yaw; Animation = Phase; }
    internal void Shift(Vector3 delta) { Anchor += delta; Patrol += delta; Position += delta; }
    public void Update(float dt)
    {
        if (Talking) return;
        Animation += dt;
        if (Pose != ResidentPose.Walking) return;
        float length = Vector3.Distance(Anchor, Patrol);
        if (length < .01f) return;
        progress += direction * dt * .32f / length;
        if (progress >= 1) { progress = 1; direction = -1; }
        if (progress <= 0) { progress = 0; direction = 1; }
        Position = Vector3.Lerp(Anchor, Patrol, progress);
        var forward = (Patrol - Anchor) * direction;
        float target = MathF.Atan2(forward.X, forward.Z);
        float delta = MathF.Atan2(MathF.Sin(target - Yaw), MathF.Cos(target - Yaw));
        Yaw += delta * (1 - MathF.Exp(-dt * 2));
    }
}

internal static class Residents
{
    public static List<Resident> Create(Sector sector, int seed)
    {
        var random = new Xor128(Sector.SeedAt(seed ^ 0x51a7, sector.X, sector.Z));
        Vector3[] anchors = [new(2, 0, 3), new(-8, 0, 24), new(-14, 0, -22)];
        int[] firstProfiles = [0, 5, 7];
        var result = new List<Resident>();
        bool origin = sector.X == 0 && sector.Z == 0;
        for (int i = 0; i < anchors.Length; i++)
        {
            if (!origin && (result.Count >= 2 || random.NextDouble() < .65)) continue;
            int profile = origin ? firstProfiles[i] : random.Next(Dialogue.Lines.Length);
            var speech = Dialogue.Lines[profile];
            ResidentPose pose = speech.Pose;
            ResidentAge age = speech.Age;
            bool female = speech.Gender == "female";
            float scale = age == ResidentAge.Child ? .68f : female ? .96f : 1.04f;
            Vector3 a = sector.Transform(anchors[i]), b = sector.Transform(anchors[i] + new Vector3(0, 0, 2.5f));
            if (pose != ResidentPose.Walking) b = a;
            // Check the entire body footprint and every point of a walking route.
            if (!Supported(sector.Collision, a, b, scale * (pose == ResidentPose.Collapsed ? 1.05f : .48f))) continue;
            var resident = new Resident { Id = Sector.ValueAt(seed ^ i, sector.X, sector.Z), Profile = profile, Anchor = a, Patrol = b,
                Pose = pose, Age = age, Female = female, Scale = scale, Phase = random.NextSingle() * 6 };
            resident.Initialize(random.NextSingle() * MathF.Tau);
            result.Add(resident);
        }
        return result;
    }
    internal static bool Supported(WalkableScene scene, Vector3 a, Vector3 b, float radius)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a, b) / .25f));
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, (float)i / steps);
            foreach (float dx in new[] { -radius, 0, radius })
                foreach (float dz in new[] { -radius, 0, radius })
                {
                    float x = p.X + dx, z = p.Z + dz;
                    if (Math.Abs(scene.FloorAt(x, z, p.Y + .05f) - p.Y) > .04f || scene.Blocked(new(x, p.Y + Explorer.EyeHeight, z))) return false;
                }
            if (scene.CeilingAt(p.X, p.Z, p.Y + .1f) < p.Y + 2) return false;
        }
        return true;
    }
}

internal sealed class Conversation
{
    public Resident? Speaker { get; private set; }
    public int Page { get; private set; }
    public float Elapsed { get; private set; }
    public string Text => Speaker == null ? "" : Dialogue.Lines[Speaker.Profile].Pages[Page];
    public int Revealed => Math.Min(Text.Length, (int)(Elapsed * 28));
    public void Start(Resident resident) { Close(); Speaker = resident; resident.Talking = true; Page = 0; Elapsed = 0; }
    public void Update(float dt) => Elapsed += dt;
    public void Advance()
    {
        if (Speaker == null) return;
        if (Revealed < Text.Length) { Elapsed = Text.Length / 28f + 1; return; }
        if (++Page >= Dialogue.Lines[Speaker.Profile].Pages.Length) Close();
        else Elapsed = 0;
    }
    public void Close() { if (Speaker != null) Speaker.Talking = false; Speaker = null; Page = 0; }
    public static Resident? Target(World world, Vector3 eye, Vector3 forward)
    {
        return world.People.Where(p => Vector3.DistanceSquared(p.LookPoint, eye) < 3.4f * 3.4f)
            .Where(p => Vector3.Dot(Vector3.Normalize(p.LookPoint - eye + new Vector3(.00001f)), forward) > .35f)
            .OrderBy(p => Vector3.DistanceSquared(p.LookPoint, eye))
            .FirstOrDefault(p => world.Sectors.All(s => !s.Collision.Obstructs(eye, p.LookPoint)));
    }
}
