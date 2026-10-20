using System.Numerics;

namespace Caero;

internal enum PredatorState { Patrol, Approaching, Stalking, Hunting }

// One predator follows the streamed neighborhood; no population accumulates.
internal sealed partial class SeaPredator
{
    public const float ApproachDelay = 5, StalkDelay = 15, AttackDelay = 25, Proximity = 28, Radius = 7;
    internal const float CruiseHeight = World.SeaFloor + 5;
    private const float MinimumY = World.SeaFloor + 4.2f;
    public Vector3 Position { get; private set; }
    public Vector3 Forward { get; private set; } = Vector3.UnitZ;
    public Vector3 Mouth => Position + Forward * 8.5f - Vector3.UnitY * .4f;
    public PredatorState State { get; private set; }
    public float DiveSeconds => (float)diveSeconds;
    private double diveSeconds;
    public float Time { get; private set; }
    public float Gape => State == PredatorState.Hunting ? DayNightCycle.Smooth(stateTime / .6f) : State == PredatorState.Stalking ? .12f : .025f;
    internal Vector3 IntentTarget { get; private set; }
    public bool Active { get; private set; }
    private Vector3 home;
    private float stateTime, grace = 3;
    private bool initialized;
    private bool entryArmed;
    private float spawnRetry;
    internal int EntrySpawnCount { get; private set; }

    public void Reset(World world, Vector3 near, float safetySeconds = 3, bool waterEntry = false)
    {
        Active = false; initialized = true; diveSeconds = 0; grace = safetySeconds;
        State = PredatorState.Patrol; stateTime = 0;
        entryArmed = near.Y > World.WaterLevel + .25f;
        spawnRetry = 1;
        ClearNavigation();
        int cx = (int)MathF.Round((near.X - 96) / 192), cz = (int)MathF.Round((near.Z - 96) / 192);
        var sites = from x in Enumerable.Range(cx - 1, 3) from z in Enumerable.Range(cz - 1, 3)
            let p = new Vector3(x * 192 + 96, CruiseHeight, z * 192 + 96)
            orderby Vector2.DistanceSquared(new(p.X, p.Z), new(near.X, near.Z)) select p;
        foreach (var site in sites)
            foreach (float y in new[] { CruiseHeight, CruiseHeight + 1, CruiseHeight + 3 })
            {
                var point = new Vector3(site.X, y, site.Z);
                long sx = (long)MathF.Round((site.X - 96) / World.CellSize), sz = (long)MathF.Round((site.Z - 96) / World.CellSize);
                float angle = (uint)Sector.ValueAt(world.Seed, checked(world.OriginX + sx), checked(world.OriginZ + sz)) / (float)uint.MaxValue * MathF.Tau;
                for (int turn = 0; turn < 4; turn++)
                {
                    float facing = angle + turn * MathF.PI / 2;
                    if (TrySpawn(world, point, new(MathF.Sin(facing), 0, MathF.Cos(facing)))) return;
                }
            }
        if (waterEntry) TryEntrySite(world, near);
    }
    private bool TrySpawn(World world, Vector3 point, Vector3 forward)
    {
        if (!Clear(world, point, forward)) return false;
        Position = home = IntentTarget = point; Forward = forward; Active = true;
        return true;
    }
    private bool TryEntrySite(World world, Vector3 near)
    {
        var cell = world.GlobalCell(near);
        float start = (uint)Sector.ValueAt(world.Seed, cell.X, cell.Z) / (float)uint.MaxValue * MathF.Tau;
        foreach (float radius in new[] { 0f, 24f, 40f, 56f, 72f, 96f })
            for (int site = 0; site < (radius == 0 ? 1 : 16); site++)
            {
                float angle = start + site * MathF.Tau / 16;
                Vector3 point = new(near.X + MathF.Sin(angle) * radius, CruiseHeight, near.Z + MathF.Cos(angle) * radius);
                float heading = radius == 0 ? start : angle + MathF.PI;
                for (int turn = 0; turn < 4; turn++)
                {
                    float facing = heading + turn * MathF.PI / 2;
                    if (TrySpawn(world, point, new(MathF.Sin(facing), 0, MathF.Cos(facing)))) return true;
                }
            }
        return false;
    }
    internal static bool Clear(World world, Vector3 position, Vector3 forward)
    {
        if (position.Y < MinimumY || position.Y > World.WaterLevel - 4) return false;
        // The broad geometry spheres include the wide fins, but greatly
        // overestimate vertical thickness. Use the actual upright mesh's lower
        // envelope for the flat seabed, including the fully open jaw and tail.
        float bottom = position.Y - 3.8f * MathF.Sqrt(Math.Max(0, 1 - forward.Y * forward.Y))
            - Math.Max(forward.Y * 15.7f, -forward.Y * 9.2f);
        if (bottom < World.SeaFloor + .2f) return false;
        foreach (float z in new[] { -11f, -5f, 1f, 7f })
            if (world.SphereBlocked(position + forward * z, Radius, includeSeabed: false)) return false;
        return true;
    }
    private bool SweptClear(World world, Vector3 next, Vector3 direction)
    {
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Forward, direction), -1, 1));
        if (!Clear(world, next, direction)) return false;
        int samples = Math.Max(1, Math.Max((int)MathF.Ceiling(angle / .08f), (int)MathF.Ceiling(Vector3.Distance(Position, next) / .2f)));
        Vector3 axis = Vector3.Cross(Forward, direction);
        if (axis.LengthSquared() < .00001f) axis = Vector3.UnitY;
        axis = Vector3.Normalize(axis);
        for (int i = 1; i <= samples; i++)
        {
            float t = (float)i / samples;
            var facing = Vector3.Transform(Forward, Quaternion.CreateFromAxisAngle(axis, angle * t));
            if (!Clear(world, Vector3.Lerp(Position, next, t), facing)) return false;
        }
        return true;
    }
    public bool Update(float dt, World world, Explorer player)
    {
        if (dt <= 0) return false;
        if (!initialized) Reset(world, player.Position);
        // Prepare just before the head enters water. Hysteresis prevents bobbing
        // from respawning the fish and restarting the dive every frame.
        if (player.Position.Y > World.WaterLevel + .4f) entryArmed = true;
        if (entryArmed && player.Position.Y < World.WaterLevel + .25f)
        {
            Reset(world, player.Position, grace, waterEntry: true);
            entryArmed = false; EntrySpawnCount++;
        }
        spawnRetry -= dt;
        if ((!Active && spawnRetry <= 0) || (Active && Vector3.DistanceSquared(Position, player.Position) > 384 * 384))
        {
            // Streaming relocation is still the same continuous dive.
            double elapsed = diveSeconds;
            var state = State; float age = stateTime; bool armed = entryArmed;
            Reset(world, player.Position, grace, waterEntry: true);
            diveSeconds = elapsed;
            State = state; stateTime = age; entryArmed = armed;
        }
        if (!Active) return false;
        int steps = Math.Max(1, (int)MathF.Ceiling(dt / (1f / 30)));
        for (int i = 0; i < steps; i++)
            if (Step(dt / steps, world, player)) return true;
        return false;
    }
    private bool Step(float dt, World world, Explorer player)
    {
        Time += dt; stateTime += dt; grace = Math.Max(0, grace - dt);
        bool submerged = player.Position.Y < World.WaterLevel - .4f;
        diveSeconds = submerged ? diveSeconds + dt : 0;
        double seconds = diveSeconds + .000001; // Absorb only floating-point timestep roundoff.
        bool near = Vector3.DistanceSquared(Position, player.Position) < Proximity * Proximity;
        var state = !submerged || grace > 0 ? PredatorState.Patrol
            : State == PredatorState.Hunting || near || seconds >= AttackDelay ? PredatorState.Hunting
            : seconds >= StalkDelay ? PredatorState.Stalking
            : seconds >= ApproachDelay ? PredatorState.Approaching : PredatorState.Patrol;
        if (State != state) { State = state; stateTime = 0; ClearNavigation(); }

        Vector3 target;
        if (State == PredatorState.Patrol)
        {
            ClearNavigation();
            target = new(home.X + MathF.Sin(Time * .065f) * 16, CruiseHeight + MathF.Sin(Time * .13f) * .25f,
                home.Z + MathF.Cos(Time * .065f) * 16);
        }
        else if (State == PredatorState.Approaching) target = RegionTarget(world, player.Position);
        else if (State == PredatorState.Stalking) target = new(player.Position.X, CruiseHeight, player.Position.Z);
        // Steer toward the prey itself. Subtracting a moving mouth offset makes
        // the target orbit around the fish and can prevent it ever lining up.
        else target = player.Position - Vector3.UnitY * .5f;
        target.Y = Math.Clamp(target.Y, MinimumY, State == PredatorState.Hunting ? World.WaterLevel - 4 : CruiseHeight + .25f);
        IntentTarget = target;
        if (State != PredatorState.Patrol) target = PursuitTarget(world, target, dt);
        Vector3 desired = target - Position;
        if (desired.LengthSquared() > .001f)
        {
            desired = Vector3.Normalize(desired);
            float vertical = State == PredatorState.Hunting ? Math.Clamp(desired.Y, -.4f, .4f) : 0;
            if (Position.Y < World.SeaFloor + 12) vertical = 0;
            var horizontal = new Vector2(desired.X, desired.Z);
            if (horizontal.LengthSquared() < .00001f) horizontal = new(Forward.X, Forward.Z);
            horizontal = Vector2.Normalize(horizontal) * MathF.Sqrt(1 - vertical * vertical);
            desired = new(horizontal.X, vertical, horizontal.Y);
            float speed = State switch { PredatorState.Hunting => 11, PredatorState.Stalking => 4, PredatorState.Approaching => 2, _ => 1.1f };
            float depthSpeed = State == PredatorState.Hunting ? 4.4f : 1.8f;
            if (State is PredatorState.Approaching or PredatorState.Stalking && PlanarDistance(Position, target) < 5) speed = 0;
            if (State == PredatorState.Hunting)
            {
                speed *= Math.Clamp(Vector3.Dot(Forward, desired), .12f, 1);
                // If the swimmer has entered beside/inside the long body, back
                // off while facing them so the mouth can reach them, not orbit.
                if (Vector3.DistanceSquared(Position, player.Position) < 7.5f * 7.5f) speed = -Math.Min(speed, 3);
            }
            // Turn in yaw and pitch separately: lerp cannot reverse an opposite
            // heading, while a shortest 3D arc can attempt a loop into the seabed.
            float yawNow = MathF.Atan2(Forward.X, Forward.Z), yawTarget = MathF.Atan2(desired.X, desired.Z);
            float yawDelta = MathF.Atan2(MathF.Sin(yawTarget - yawNow), MathF.Cos(yawTarget - yawNow));
            float pitchNow = MathF.Asin(Math.Clamp(Forward.Y, -1, 1)), pitchTarget = MathF.Asin(desired.Y);
            float turnAngle = dt * (State == PredatorState.Hunting ? 1.35f : .9f);
            float yawNext = yawNow + Math.Clamp(yawDelta, -turnAngle, turnAngle);
            float pitchNext = pitchNow + Math.Clamp(pitchTarget - pitchNow, -turnAngle, turnAngle);
            Vector3 smooth = new(MathF.Sin(yawNext) * MathF.Cos(pitchNext), MathF.Sin(pitchNext), MathF.Cos(yawNext) * MathF.Cos(pitchNext));
            // Keep the whole swept body clear, including when turning near pillars.
            bool moved = false;
            foreach (float yaw in new[] { 0f, .4f, -.4f, .8f, -.8f, 1.3f, -1.3f, MathF.PI })
            {
                Vector3 direction = Vector3.Transform(smooth, Matrix4x4.CreateRotationY(yaw));
                Vector3 next = Position + direction * speed * dt;
                // Fins can change depth while the body remains level. Otherwise
                // the seabed prevents pitching and also prevents any ascent/descent.
                next.Y = Math.Clamp(Position.Y + Math.Clamp(target.Y - Position.Y, -depthSpeed * dt, depthSpeed * dt), MinimumY, World.WaterLevel - 4);
                if (!SweptClear(world, next, direction)) continue;
                Forward = direction; Position = next; moved = true; break;
            }
            if (!moved)
            {
                // Near the seabed, pitching upward lowers the long tail into the
                // floor. Rise level first rather than backing down into a deadlock.
                Vector3 lift = Position + Vector3.UnitY * Math.Clamp(target.Y - Position.Y, -depthSpeed * dt, depthSpeed * dt);
                if (Math.Abs(lift.Y - Position.Y) > .0001f && SweptClear(world, lift, Forward)) Position = lift;
                else
                {
                    Vector3 back = Position - Forward * Math.Abs(speed) * dt * .5f;
                    if (SweptClear(world, back, Forward)) Position = back;
                }
            }
        }
        Vector3 body = player.Position - Vector3.UnitY * .9f;
        if (State == PredatorState.Hunting && Gape > .6f && submerged && grace == 0
            && Vector3.DistanceSquared(Mouth, body) < 3.5f * 3.5f
            && world.Sectors.All(s => !s.Collision.Obstructs(Mouth, body)))
        {
            world.ReturnToStart(player, 10);
            return true;
        }
        return false;
    }
    // Deterministic preview pose; also permits regression checks at the mouth.
    internal void Stage(Vector3 position, Vector3 forward, PredatorState state)
    {
        Position = home = position; Forward = Vector3.Normalize(forward); State = state;
        initialized = Active = true; grace = 0; stateTime = 2;
        entryArmed = false;
        diveSeconds = state switch { PredatorState.Approaching => ApproachDelay, PredatorState.Stalking => StalkDelay, PredatorState.Hunting => AttackDelay, _ => 0 };
        ClearNavigation();
    }
    internal void Shift(Vector3 delta)
    {
        Position += delta; home += delta; IntentTarget += delta; navigationGoal += delta;
        for (int i = 0; i < navigation.Count; i++) navigation[i] += delta;
    }
}
