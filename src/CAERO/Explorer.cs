using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed class Explorer
{
    public const float EyeHeight = 1.75f;
    // Apex height scales with speed squared: 6 * sqrt(1.5) gives 1.5x the original jump.
    public const float JumpSpeed = 7.35f, Gravity = 18;
    private const float WaterDrag = 1.6f;
    public Vector3 Position = new(0, 1.75f, 29);
    public float Yaw, Pitch;
    public bool Flying;
    public bool Swimming { get; private set; }
    public bool Grounded { get; private set; }
    public bool Jumped { get; private set; }
    public bool FlightBumped { get; private set; }
    public bool FlightStarted { get; private set; }
    public bool FlightStopped { get; private set; }
    public float LandingImpact { get; private set; }
    public float SplashImpact { get; private set; }
    public bool Surfaced { get; private set; }
    private bool? headUnderwater;
    public bool Captured { get; private set; } = true;
    internal void SetCaptured(bool captured)
    { Captured = captured; if (captured) Raylib.DisableCursor(); else Raylib.EnableCursor(); }
    private float verticalSpeed;
    private bool jumping, potionDrunk;
    internal void DrinkPotion() => potionDrunk = true;
    internal bool ConsumePotionDrink() { bool result = potionDrunk; potionDrunk = false; return result; }
    public Vector3 Forward => new(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), -MathF.Cos(Yaw) * MathF.Cos(Pitch));
    public Camera3D Camera => new() { Position = Position, Target = Position + Forward,
        Up = Vector3.UnitY, FovY = 62, Projection = CameraProjection.Perspective };

    public void Restore(SavedView view)
    {
        Position = new(view.X, view.Y, view.Z); Yaw = view.Yaw;
        Pitch = Math.Clamp(view.Pitch, -1.48f, 1.48f); Flying = view.Flying;
        verticalSpeed = 0; jumping = false; Swimming = false; ResetAudioEvents(); headUnderwater = null; Grounded = false;
    }
    public void GoTo(Viewpoint view)
    {
        Position = view.Position;
        Vector3 direction = Vector3.Normalize(view.Target - Position);
        Yaw = MathF.Atan2(direction.X, -direction.Z); Pitch = MathF.Asin(direction.Y);
        Flying = false; verticalSpeed = 0; jumping = false; Swimming = false; ResetAudioEvents(); headUnderwater = null; Grounded = false;
    }
    public void Update(float dt, World world)
    {
        ResetAudioEvents();
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        { Captured = !Captured; if (Captured) Raylib.DisableCursor(); else Raylib.EnableCursor(); }
        if (!Captured && Raylib.IsMouseButtonPressed(MouseButton.Left))
        { Captured = true; Raylib.DisableCursor(); }
        if (!Captured || !Raylib.IsWindowFocused()) return;
        var delta = Raylib.GetMouseDelta();
        Yaw += delta.X * 0.0022f; Pitch = Math.Clamp(Pitch - delta.Y * 0.0022f, -1.48f, 1.48f);
        var forward = Flying || Position.Y - EyeHeight < World.WaterLevel - .25f
            ? Forward : new Vector3(MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        var right = new Vector3(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
        Vector3 move = Vector3.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W)) move += forward;
        if (Raylib.IsKeyDown(KeyboardKey.S)) move -= forward;
        if (Raylib.IsKeyDown(KeyboardKey.D)) move += right;
        if (Raylib.IsKeyDown(KeyboardKey.A)) move -= right;
        if (move.LengthSquared() > 0) move = Vector3.Normalize(move);
        AdvanceControls(dt, world, move, Raylib.IsKeyDown(KeyboardKey.Space), Raylib.IsKeyPressed(KeyboardKey.Space),
            false, Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift),
            Raylib.IsKeyPressed(KeyboardKey.F));
    }

    internal bool ToggleFlight(World world)
    {
        if (Flying) { Flying = false; verticalSpeed = 0; return true; }
        // Walking off an edge, swimming and standing still are not jumps.
        if (!world.Progress.CanFly || !jumping || Grounded || Swimming) return false;
        Flying = true; jumping = false; verticalSpeed = 0; return true;
    }
    internal void AdvanceControls(float dt, World world, Vector3 move, bool rise, bool jump, bool descend, bool fast, bool flightPressed)
    {
        bool wasFlying = Flying;
        bool toggled = flightPressed && ToggleFlight(world);
        // Mouse look steers flight. Movement keys cannot stop or reverse it.
        Advance(dt, world, Flying ? Forward : move, rise, jump, descend, fast);
        // Advance clears the previous frame's events before simulating motion.
        FlightStarted = toggled && !wasFlying;
        FlightStopped = toggled && wasFlying;
    }

    // Input-independent simulation is also used by the movement regression checks.
    public void Advance(float dt, World world, Vector3 move, bool rise, bool jump, bool descend, bool fast)
    {
        ResetAudioEvents();
        if (dt <= 0) return;
        if (move.LengthSquared() > 1) move = Vector3.Normalize(move);
        float speed = fast ? 13 + world.Progress.DashBonus : 5;
        float swimSpeed = fast ? 9 + world.Progress.DashBonus : 4;
        if (Flying)
        {
            dt = AdvanceFlight(dt, world, move * speed);
            if (Flying || dt <= 0)
            {
                world.Recenter(this);
                return;
            }
        }
        TrackSurface();
        // Bound both horizontal and vertical travel, including long falls.
        float maximumSpeed = Math.Max(speed, Math.Abs(verticalSpeed) + Gravity * dt);
        float jumpSpeed = JumpSpeed * world.Progress.JumpMultiplier;
        if (jump) maximumSpeed = Math.Max(maximumSpeed, jumpSpeed);
        int steps = Math.Max(1, (int)MathF.Ceiling(maximumSpeed * dt / .10f));
        for (int i = 0; i < steps; i++)
        {
            float step = dt / steps;
            float feet = Position.Y - EyeHeight;
            float floor = world.SupportBelow(Position.X, Position.Z, feet + .05f);
            bool grounded = feet <= floor + .05f && verticalSpeed <= 0;
            UpdateSwimming();
            var horizontal = new Vector3(move.X, 0, move.Z);
            if (Swimming)
            {
                // Keep impact momentum on entry. Drag gradually brings vertical motion
                // toward buoyancy or swimming input instead of cancelling the fall.
                float buoyancy = Math.Clamp((World.WaterLevel + .6f - Position.Y) * 2, -2, 2);
                float targetSpeed = rise ? 5 : descend ? -5 : move.Y * swimSpeed + buoyancy;
                verticalSpeed = targetSpeed + (verticalSpeed - targetSpeed) * MathF.Exp(-WaterDrag * step);
            }
            else
            {
                if (grounded && jump && i == 0) { verticalSpeed = jumpSpeed; Jumped = true; jumping = true; }
                verticalSpeed = Math.Max(-35, verticalSpeed - Gravity * step);
            }
            float travel = (Swimming ? swimSpeed : speed) * step;
            MoveHorizontal(world, new(horizontal.X * travel, 0, 0), grounded || Swimming);
            MoveHorizontal(world, new(0, 0, horizontal.Z * travel), grounded || Swimming);
            feet = Position.Y - EyeHeight;
            float ceiling = world.CeilingAbove(Position);
            Position.Y += verticalSpeed * step;
            if (Position.Y > ceiling - .02f) { Position.Y = ceiling - .02f; verticalSpeed = Math.Min(0, verticalSpeed); }
            floor = world.SupportBelow(Position.X, Position.Z, feet + .05f);
            if (verticalSpeed <= 0 && Position.Y - EyeHeight <= floor)
            {
                if (!Swimming && verticalSpeed < -2) LandingImpact = Math.Max(LandingImpact, -verticalSpeed);
                Position.Y = floor + EyeHeight; verticalSpeed = 0; jumping = false;
            }
            TrackSurface();
            if (Position.Y < World.SeaFloor - 10)
            { world.ReturnToStart(this); }
        }
        UpdateSwimming();
        float support = world.SupportBelow(Position.X, Position.Z, Position.Y - EyeHeight + .05f);
        Grounded = !Swimming && verticalSpeed <= 0 && Position.Y - EyeHeight <= support + .05f;
        world.Recenter(this);
    }

    private float AdvanceFlight(float dt, World world, Vector3 velocity)
    {
        Swimming = false; Grounded = false; headUnderwater = null;
        int steps = Math.Max(1, (int)MathF.Ceiling(velocity.Length() * dt / .1f));
        float step = dt / steps;
        Vector3 direction = velocity.LengthSquared() > 0 ? Vector3.Normalize(velocity) : Vector3.Zero;
        const float clearance = .25f;
        float waterEntry = World.WaterLevel + EyeHeight - .25f;
        for (int i = 0; i < steps; i++)
        {
            Vector3 next = Position + velocity * step;
            Vector3 probe = next + direction * clearance;
            float floor = world.SupportBelow(probe.X, probe.Z, Position.Y - EyeHeight + clearance);
            if (world.Blocked(probe) || probe.Y - EyeHeight < floor
                || probe.Y > world.CeilingAbove(Position) - .02f)
            {
                Flying = false; verticalSpeed = 0; FlightBumped = true;
                return 0;
            }
            if (velocity.Y < 0 && Position.Y >= waterEntry && next.Y < waterEntry)
            {
                float fraction = (Position.Y - waterEntry) / (Position.Y - next.Y);
                Position = Vector3.Lerp(Position, next, fraction);
                Flying = false; verticalSpeed = velocity.Y;
                // Start the existing water physics with the actual flight impact speed.
                Position.Y = waterEntry - .0001f;
                TrackSurface(); UpdateSwimming();
                return dt - step * (i + fraction);
            }
            Position = next;
        }
        return 0;
    }

    internal void ClearMotionEvents() => ResetAudioEvents();
    private void ResetAudioEvents()
    {
        SplashImpact = 0; Surfaced = false; Jumped = false;
        FlightBumped = FlightStarted = FlightStopped = false; LandingImpact = 0;
    }
    private void TrackSurface()
    {
        // Hysteresis prevents repeated exit sounds while bobbing at the waterline.
        headUnderwater ??= Position.Y < World.WaterLevel - .08f;
        if (Position.Y < World.WaterLevel - .08f) headUnderwater = true;
        else if (headUnderwater == true && Position.Y > World.WaterLevel + .08f)
        { Surfaced = true; headUnderwater = false; }
    }
    private void UpdateSwimming()
    {
        bool next = Position.Y - EyeHeight < World.WaterLevel - .25f;
        if (next && !Swimming && verticalSpeed < -1) SplashImpact = Math.Max(SplashImpact, -verticalSpeed);
        Swimming = next;
        if (next) jumping = false;
    }

    private void MoveHorizontal(World world, Vector3 delta, bool canStep)
    {
        if (delta.LengthSquared() < 1e-10f) return;
        Vector3 next = Position + delta;
        float feet = Position.Y - EyeHeight;
        const float stepHeight = .42f;
        float floor = world.SurfaceBelow(next.X, next.Z, feet + stepHeight);
        if (canStep)
        {
            Vector3 probe = next + Vector3.Normalize(delta) * .35f;
            floor = Math.Max(floor, world.SurfaceBelow(probe.X, probe.Z, feet + stepHeight));
        }
        if (canStep && floor > feet && floor <= feet + stepHeight)
        {
            Vector3 raised = new(next.X, floor + EyeHeight, next.Z);
            if (!world.Blocked(raised)) { Position = raised; verticalSpeed = Math.Max(0, verticalSpeed); return; }
        }
        if (!world.Blocked(next)) { Position.X = next.X; Position.Z = next.Z; return; }
    }
}
