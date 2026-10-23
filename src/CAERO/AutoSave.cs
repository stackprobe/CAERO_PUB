namespace Caero;

internal sealed class AutoSave(string? filePath = null)
{
    internal const float Interval = 10;
    private float elapsed;
    private SavedView? stable;
    internal void Update(float dt, Explorer player, World world)
    {
        elapsed += Math.Max(0, dt);
        if (elapsed >= Interval && !world.WarpInProgress) Flush(player, world);
    }
    internal bool Flush(Explorer player, World world)
    {
        // A warp's source was saved before departure. Never overwrite it with
        // an arrival position whose surrounding floors are still being loaded.
        if (!world.WarpInProgress) stable = SaveStore.Capture(player, world);
        elapsed = 0;
        if (stable == null) return false;
        bool success = SaveStore.Save(stable with { Progress = world.Progress.Snapshot() }, filePath);
        if (!success) Console.Error.WriteLine("AUTOSAVE: " + SaveStore.LastError);
        return success;
    }
}
