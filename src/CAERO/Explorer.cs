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
    public float LandingImpact { get; private set; }
    public float SplashImpact { get; private set; }
    public bool Surfaced { get; private set; }
    private bool? headUnderwater;
    public bool Captured { get; private set; } = true;
    private float verticalSpeed;
    public Vector3 Forward => new(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), -MathF.Cos(Yaw) * MathF.Cos(Pitch));
    public Camera3D Camera => new() { Position = Position, Target = Position + Forward,
        Up = Vector3.UnitY, FovY = 62, Projection = CameraProjection.Perspective };

    public void Restore(SavedView view)
    {
        Position = new(view.X, view.Y, view.Z); Yaw = view.Yaw;
        Pitch = Math.Clamp(view.Pitch, -1.48f, 1.48f); Flying = view.Flying;
        verticalSpeed = 0; Swimming = false; ResetAudioEvents(); headUnderwater = null; Grounded = false;
    }
    public void GoTo(Viewpoint view)
    {
        Position = view.Position;
        Vector3 direction = Vector3.Normalize(view.Target - Position);
        Yaw = MathF.Atan2(direction.X, -direction.Z); Pitch = MathF.Asin(direction.Y);
        Flying = false; verticalSpeed = 0; Swimming = false; ResetAudioEvents(); headUnderwater = null; Grounded = false;
    }
    public void Update(float dt, World world)
    {
        ResetAudioEvents();
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        { Captured = !Captured; if (Captured) Raylib.DisableCursor(); else Raylib.EnableCursor(); }
        if (!Captured && Raylib.IsMouseButtonPressed(MouseButton.Left))
        { Captured = true; Raylib.DisableCursor(); }
        if (Raylib.IsKeyPressed(KeyboardKey.F)) { Flying = !Flying; verticalSpeed = 0; }
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
        if (Flying)
        {
            if (Raylib.IsKeyDown(KeyboardKey.Space)) move += Vector3.UnitY;
            if (Raylib.IsKeyDown(KeyboardKey.C)) move -= Vector3.UnitY;
        }
        if (move.LengthSquared() > 0) move = Vector3.Normalize(move);
        Advance(dt, world, move, Raylib.IsKeyDown(KeyboardKey.Space), Raylib.IsKeyPressed(KeyboardKey.Space),
            Raylib.IsKeyDown(KeyboardKey.C), Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift),
            Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl));
    }

    // Input-independent simulation is also used by the movement regression checks.
    public void Advance(float dt, World world, Vector3 move, bool rise, bool jump, bool descend, bool fast, bool turbo = false)
    {
        ResetAudioEvents();
        if (dt <= 0) return;
        if (move.LengthSquared() > 1) move = Vector3.Normalize(move);
        float speed = turbo ? 50 : fast ? 13 : 5;
        float swimSpeed = turbo ? 40 : fast ? 9 : 4;
        if (Flying)
        {
            Swimming = false; Grounded = false; headUnderwater = null; Position += move * speed * dt;
            world.Recenter(this);
            return;
        }
        TrackSurface();
        // Bound both horizontal and vertical travel, including long falls.
        float maximumSpeed = Math.Max(speed, Math.Abs(verticalSpeed) + Gravity * dt);
        if (jump) maximumSpeed = Math.Max(maximumSpeed, JumpSpeed);
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
                float verticalSwimSpeed = turbo ? 50 : 5;
                float targetSpeed = rise ? verticalSwimSpeed : descend ? -verticalSwimSpeed : move.Y * swimSpeed + buoyancy;
                verticalSpeed = targetSpeed + (verticalSpeed - targetSpeed) * MathF.Exp(-WaterDrag * step);
            }
            else
            {
                if (grounded && jump && i == 0) { verticalSpeed = JumpSpeed; Jumped = true; }
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
                Position.Y = floor + EyeHeight; verticalSpeed = 0;
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

    internal void ClearMotionEvents() => ResetAudioEvents();
    private void ResetAudioEvents() { SplashImpact = 0; Surfaced = false; Jumped = false; LandingImpact = 0; }
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
