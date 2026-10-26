using System.Numerics;

namespace Caero;

internal enum ResidentPose { Walking, Sitting, Standing }
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
    internal string? SpecialId { get; init; }
    internal bool Huddled { get; init; }
    internal Dialogue.Speech? SpecialSpeech { get; init; }
    internal Dialogue.Speech Speech => SpecialSpeech ?? Dialogue.Lines[Profile];
    public Vector3 Position { get; private set; }
    public float Yaw { get; private set; }
    public float Animation { get; private set; }
    public bool Talking { get; set; }
    public string Name => Speech.Name;
    public Vector3 LookPoint => Position + Vector3.UnitY * Scale * (Huddled ? .7f : Pose == ResidentPose.Sitting ? .8f : 1.4f);
    // A small body core leaves room on both sides of a three-unit sky bridge.
    internal float CollisionRadius => .28f * Scale;
    internal float CollisionTop => Position.Y + Scale * (Pose == ResidentPose.Sitting ? 1.12f : 1.72f);
    internal Vector3 CollisionCenter => Position + (Pose == ResidentPose.Sitting
        ? new Vector3(MathF.Sin(Yaw), 0, MathF.Cos(Yaw)) * (.14f * Scale) : Vector3.Zero);
    internal bool SharesColumn(float x, float z)
    {
        var center = CollisionCenter;
        float radius = CollisionRadius + .30f; // Player radius matches WalkableScene.
        return (x - center.X) * (x - center.X) + (z - center.Z) * (z - center.Z) < radius * radius;
    }
    internal bool Blocks(Vector3 eye) => eye.Y > Position.Y + .02f
        && eye.Y - Explorer.EyeHeight < CollisionTop - .02f && SharesColumn(eye.X, eye.Z);
    private float progress;
    private int direction = 1;
    public void Initialize(float yaw)
    { Position = Anchor; Yaw = yaw; Animation = Phase; }
    internal void Shift(Vector3 delta) { Anchor += delta; Patrol += delta; Position += delta; }
    public void Update(float dt, Vector3? playerEye = null)
    {
        if (Talking) return;
        Animation += dt;
        if (Pose != ResidentPose.Walking) return;
        float length = Vector3.Distance(Anchor, Patrol);
        if (length < .01f) return;
        float oldProgress = progress;
        progress += direction * dt * .32f / length;
        if (progress >= 1) { progress = 1; direction = -1; }
        if (progress <= 0) { progress = 0; direction = 1; }
        Vector3 oldPosition = Position;
        Position = Vector3.Lerp(Anchor, Patrol, progress);
        if (playerEye is { } eye && Blocks(eye))
        { Position = oldPosition; progress = oldProgress; return; }
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
        int population = random.Next(6);
        var areas = sector.ResidentAreas.GroupBy(a => a.Kind).Select(g => g.ToArray()).ToArray();
        var result = new List<Resident>();
        for (int i = 0; i < 192 && result.Count < population; i++)
        {
            int profile = random.Next(Dialogue.Lines.Length);
            var speech = Dialogue.Lines[profile];
            ResidentPose pose = speech.Pose;
            ResidentAge age = speech.Age;
            bool female = speech.Gender == "female";
            float scale = age == ResidentAge.Child ? .68f : female ? .96f : 1.04f;
            var category = areas[random.Next(areas.Length)];
            var area = category[random.Next(category.Length)];
            float yaw = random.NextSingle() * MathF.Tau;
            Vector3 a = sector.Transform(area.Sample(random));
            Vector3 b = a + new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)) * 2.5f;
            if (pose != ResidentPose.Walking) b = a;
            bool Allowed(Vector3 point, float facing) => AllowedFootprint(sector, area, point, scale, pose, facing);
            if (!Allowed(a, yaw) || !Allowed(b, yaw)) continue;
            float? seatedYaw = null;
            if (pose == ResidentPose.Sitting && TrySeatByWall(sector.Collision, a, scale, out var seat, out float facing, Allowed))
            { a = b = seat; seatedYaw = facing; }
            // Check the entire body footprint and every point of a walking route.
            if (pose == ResidentPose.Sitting ? !SittingSupported(sector.Collision, a, scale, seatedYaw ?? yaw)
                : !Supported(sector.Collision, a, b, scale * .48f)) continue;
            if (!AllowedPath(sector, area, a, b, scale, pose, seatedYaw ?? yaw)) continue;
            if (result.Any(p => Vector3.Distance(p.Anchor, a) - Vector3.Distance(p.Anchor, p.Patrol)
                - Vector3.Distance(a, b) < 15)) continue;
            var resident = new Resident { Id = Sector.ValueAt(seed ^ i, sector.X, sector.Z), Profile = profile, Anchor = a, Patrol = b,
                Pose = pose, Age = age, Female = female, Scale = scale, Phase = random.NextSingle() * 6 };
            resident.Initialize(seatedYaw ?? yaw);
            result.Add(resident);
        }
        return result;
    }
    internal static bool AllowedFootprint(Sector sector, ResidentArea area, Vector3 point, float scale, ResidentPose pose, float yaw)
    {
        // Check the complete footprint against semantic regions too: neither a
        // robe nor a patrol may cross a doorway into an excluded building.
        var rotation = Matrix4x4.CreateRotationY(yaw);
        float width = pose == ResidentPose.Sitting ? .42f : .48f;
        float rear = pose == ResidentPose.Sitting ? -.29f : -.48f;
        float front = pose == ResidentPose.Sitting ? .74f : .48f;
        foreach (float x in new[] { -width, 0, width })
            foreach (float z in new[] { rear, 0, front })
            {
                Vector3 p = point + Vector3.Transform(new Vector3(x, 0, z) * scale, rotation);
                if (!area.Contains(sector.ResidentLocal(p)) || !sector.ResidentFloorAllowed(p)) return false;
            }
        return true;
    }
    private static bool AllowedPath(Sector sector, ResidentArea area, Vector3 a, Vector3 b, float scale, ResidentPose pose, float yaw)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a, b) / .25f));
        for (int i = 0; i <= steps; i++)
            if (!AllowedFootprint(sector, area, Vector3.Lerp(a, b, (float)i / steps), scale, pose, yaw)) return false;
        return true;
    }
    internal static bool SittingSupported(WalkableScene scene, Vector3 seat, float scale, float yaw)
    {
        var rotation = Matrix4x4.CreateRotationY(yaw);
        Vector3 P(float x, float y, float z) => seat + Vector3.Transform(new Vector3(x, y, z) * scale, rotation);
        foreach (float x in new[] { -.41f, 0, .41f })
            foreach (float z in new[] { -.28f, 0, .24f, .48f, .73f })
            {
                var p = P(x, 0, z);
                if (Math.Abs(scene.FloorAt(p.X, p.Z, p.Y + .05f) - p.Y) > .04f) return false;
                if (scene.SphereBlocked(P(x * .8f, .13f, Math.Max(-.22f, z * .9f)), scale * .06f)) return false;
            }
        foreach (float y in new[] { .31f, .55f, .77f })
            if (scene.SphereBlocked(P(0, y, -.035f), scale * .25f)) return false;
        foreach (float side in new[] { -.245f, .245f })
            if (scene.SphereBlocked(P(side, .64f, .02f), scale * .13f)) return false;
        if (scene.SphereBlocked(P(0, .92f, .14f), scale * .23f)) return false;
        return scene.CeilingAt(seat.X, seat.Z, seat.Y + .1f) >= seat.Y + scale * 1.2f;
    }
    internal static bool TrySeatByWall(WalkableScene scene, Vector3 anchor, float scale, out Vector3 seat, out float yaw,
        Func<Vector3, float, bool>? allowed = null)
    {
        const float searchRadius = 12;
        seat = anchor; yaw = 0; float nearest = searchRadius + .01f;
        // Search existing surfaces only; keep the map and its random sequence intact.
        for (int i = 0; i < 32; i++)
        {
            float angle = i * MathF.Tau / 32;
            Vector3 toward = new(MathF.Sin(angle), 0, MathF.Cos(angle));
            Vector3 from = anchor + Vector3.UnitY * (.55f * scale);
            if (!scene.Obstructs(from, from + toward * searchRadius)) continue;
            float lo = 0, hi = searchRadius;
            for (int n = 0; n < 16; n++)
            {
                float mid = (lo + hi) / 2;
                if (scene.Obstructs(from, from + toward * mid)) hi = mid; else lo = mid;
            }
            if (hi >= nearest || hi < .3f * scale) continue;
            Vector3 candidate = anchor + toward * (hi - .29f * scale);
            float facing = MathF.Atan2(-toward.X, -toward.Z);
            if (allowed != null && !allowed(candidate, facing)) continue;
            if (!SittingSupported(scene, candidate, scale, facing)) continue;
            // A broad vertical backrest must reach both hips and shoulders.
            Vector3 across = new(toward.Z, 0, -toward.X);
            bool supported = true;
            foreach (float y in new[] { .30f, .65f })
                foreach (float side in new[] { -.13f, .13f })
                {
                    Vector3 back = candidate + (Vector3.UnitY * y + across * side) * scale;
                    if (!scene.Obstructs(back, back + toward * (.34f * scale))) supported = false;
                }
            if (!supported) continue;
            seat = candidate; yaw = facing; nearest = hi;
        }
        return nearest < searchRadius + .01f;
    }
    internal static bool Supported(WalkableScene scene, Vector3 a, Vector3 b, float radius)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a, b) / .25f));
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, (float)i / steps);
            float scale = radius / .48f;
            // Test the lower robe and torso too; an eye-height capsule alone
            // can miss low walls, beams and bridge parapets.
            if (scene.SurfaceOverlapsBox(p + new Vector3(-.4f, .03f, -.4f) * scale,
                p + new Vector3(.4f, 1.72f, .4f) * scale)) return false;
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
    internal const float InputDelay = .2f;
    internal const float ChoiceInputDelay = InputDelay;
    private float inputVisibleSeconds;
    public Resident? Speaker { get; private set; }
    internal SpecialObject? Document { get; private set; }
    private Action? confirmed;
    private string? confirmationTitle;
    internal bool Active => Speaker != null || Document != null || confirmed != null;
    internal string Title => Speaker?.Name ?? Document?.Definition.Name ?? confirmationTitle ?? "";
    private string[] pages = [];
    private DialogueChoice? choice;
    internal bool SelectedYes { get; private set; } = true;
    internal bool AwaitingChoice => Active && choice != null && Page == pages.Length - 1 && Revealed == Text.Length;
    internal bool InputReady => Active && inputVisibleSeconds >= InputDelay;
    internal bool ChoiceInputReady => AwaitingChoice && InputReady;
    internal int PageCount => pages.Length;
    public int Page { get; private set; }
    public float Elapsed { get; private set; }
    public string Text => !Active ? "" : pages[Page];
    public int Revealed => Math.Min(Text.Length, (int)(Elapsed * 28));
    public void Start(Resident resident) { Close(); Speaker = resident; resident.Talking = true; pages = resident.Speech.Pages; choice = resident.Speech.Choice; Page = 0; Elapsed = 0; }
    internal void Read(SpecialObject document) { Close(); Document = document; pages = document.Definition.Pages; choice = document.Definition.Choice; Page = 0; Elapsed = 0; }
    internal void Confirm(string title, string message, Action onYes)
    {
        Close(); confirmationTitle = title; confirmed = onYes; pages = [message];
        choice = new() { YesPages = [], NoPages = [] };
        Elapsed = message.Length / 28f + 1;
    }
    public void Update(float dt)
    {
        bool wasRevealed = Revealed == Text.Length;
        Elapsed += Math.Max(0, dt);
        // A newly completed page gets a fresh delay, including natural typewriter completion.
        inputVisibleSeconds = !wasRevealed && Revealed == Text.Length
            ? 0 : inputVisibleSeconds + Math.Max(0, dt);
    }
    internal void SelectAnswer(bool yes) { if (AwaitingChoice) SelectedYes = yes; }
    public void Advance()
    {
        if (!Active) return;
        if (Revealed < Text.Length) { Elapsed = Text.Length / 28f + 1; inputVisibleSeconds = 0; return; }
        if (AwaitingChoice)
        {
            if (confirmed is { } action)
            {
                bool yes = SelectedYes;
                Close();
                if (yes) action();
                return;
            }
            string[] branch = SelectedYes ? choice!.YesPages : choice!.NoPages;
            choice = SelectedYes ? choice!.YesChoice : choice!.NoChoice;
            SelectedYes = true;
            pages = [.. pages, .. branch];
        }
        if (++Page >= pages.Length) Close();
        else { Elapsed = 0; inputVisibleSeconds = 0; }
    }
    public void Close() { if (Speaker != null) Speaker.Talking = false; Speaker = null; Document = null; confirmed = null; confirmationTitle = null; pages = []; choice = null; SelectedYes = true; Page = 0; Elapsed = 0; inputVisibleSeconds = 0; }
    public static Resident? Target(World world, Vector3 eye, Vector3 forward)
    {
        return world.People.Where(p => Vector3.DistanceSquared(p.LookPoint, eye) < 3.4f * 3.4f)
            .Where(p => Vector3.Dot(Vector3.Normalize(p.LookPoint - eye + new Vector3(.00001f)), forward) > .35f)
            .OrderBy(p => Vector3.DistanceSquared(p.LookPoint, eye))
            .FirstOrDefault(p => world.Sectors.All(s => !s.Collision.Obstructs(eye, p.LookPoint)));
    }
}
