using System.Numerics;

namespace Caero;

internal sealed partial class World
{
    internal const double WarpMinimumSeconds = 3, WarpDepartureSeconds = .8, WarpRecoverySeconds = 1;
    private sealed class WarpTransition
    {
        internal required long X, Z;
        internal required Vector3 Forward;
        internal Task<Sector>? Generation;
        internal double Elapsed, Recovery = -1;
        internal float RecoveryStrength = 1;
        internal bool Arrived;
    }
    private WarpTransition? warp;
    private readonly List<Task<Sector>> abandonedGenerations = [];
    private string? warpFailure;
    private bool warpStarted;
    internal bool ConsumeWarpStart() { bool result = warpStarted; warpStarted = false; return result; }
    internal bool WarpInProgress => warp != null;
    internal bool WarpFalling => warp is { Arrived: true, Recovery: >= 0 };
    internal float WarpTime => (float)(warp?.Elapsed ?? 0);
    internal float WarpStrength => warp is not { } state ? 0 : state.Recovery >= 0
        ? state.RecoveryStrength * (1 - SmoothWarp(state.Recovery / WarpRecoverySeconds))
        : SmoothWarp(state.Elapsed / WarpDepartureSeconds);
    private static float SmoothWarp(double t)
    { float x = (float)Math.Clamp(t, 0, 1); return x * x * (3 - 2 * x); }

    internal bool BeginWarp(long x, long z, Vector3 forward, Func<Sector>? generate = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (warp != null) return false;
        // Finish the one existing CPU job before starting another. Nothing here
        // waits on the render thread, including when that job is still running.
        var previous = pending; pending = null;
        warpFailure = null;
        warp = new() { X = x, Z = z, Forward = forward,
            Generation = Task.Run(async () => {
                if (previous != null) (await previous.ConfigureAwait(false)).Dispose();
                return generate != null ? generate() : Sector.Generate(Seed, x, z);
            }) };
        warpStarted = true;
        return true;
    }

    internal void AdvanceWarp(double dt, Explorer player)
    {
        if (warp is not { } state) return;
        bool recovering = state.Recovery >= 0;
        state.Elapsed += Math.Max(0, dt);
        if (state.Generation is { IsCompleted: true } task)
        {
            if (!task.IsCompletedSuccessfully)
            {
                warpFailure = task.Exception?.GetBaseException().Message ?? "Warp generation was cancelled.";
                state.RecoveryStrength = WarpStrength;
                state.Generation = null; state.Recovery = 0;
            }
            else if (state.Elapsed >= WarpDepartureSeconds)
            {
                Sector ready = task.GetAwaiter().GetResult(); // Already completed; never waits.
                state.Generation = null;
                OriginX = state.X; OriginZ = state.Z;
                Evict((OriginX, OriginZ));
                foreach (var sector in sectors.Values) sector.SetOrigin(OriginX, OriginZ);
                foreach (var bridge in diagonalBridges.Values) bridge.SetOrigin(OriginX, OriginZ);
                if (sectors.TryGetValue((state.X, state.Z), out var existing)) ready.Dispose();
                else { Install(ready); existing = ready; }
                Vector3 eye = existing.WarpArrivalPosition + Vector3.UnitY * (Explorer.EyeHeight + WarpTravel.ArrivalDrop);
                player.GoTo(new("WARP ARRIVAL", eye, eye + state.Forward));
                Predator.Reset(this, player.Position, 3);
                ViewTeleported?.Invoke(player.Position);
                state.Arrived = true;
            }
        }
        // Reveal the destination only once its immediate neighborhood is ready.
        // Outer islands keep streaming in distance order after the effect ends.
        if (state.Arrived && state.Recovery < 0 && state.Elapsed >= WarpMinimumSeconds - WarpRecoverySeconds
            && Neighborhood((state.X, state.Z), LoadRadius)
                .OrderBy(cell => Vector3.DistanceSquared(WorldPosition.Offset(cell.X, cell.Z, OriginX, OriginZ), player.Position))
                .Take(9).All(sectors.ContainsKey)) state.Recovery = 0;
        if (state.Recovery >= 0)
        {
            if (recovering) state.Recovery += Math.Max(0, dt);
            if (state.Recovery >= WarpRecoverySeconds) warp = null;
        }
    }

    internal string? TakeWarpFailure()
    { string? result = warpFailure; warpFailure = null; return result; }

    private void CancelWarp()
    {
        warpStarted = false;
        if (warp?.Generation is { } task) abandonedGenerations.Add(task);
        warp = null;
    }
    private static void DisposeGeneration(Task<Sector> task)
    {
        try { task.GetAwaiter().GetResult().Dispose(); }
        catch { _ = task.Exception; } // Cancelled/failed CPU work owns no GPU resources.
    }
    private void ReleaseAbandonedGenerations()
    {
        for (int i = abandonedGenerations.Count - 1; i >= 0; i--)
            if (abandonedGenerations[i].IsCompleted)
            { DisposeGeneration(abandonedGenerations[i]); abandonedGenerations.RemoveAt(i); }
    }
}
