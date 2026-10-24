using System.Text.Json;
using System.Numerics;

namespace Caero;

internal sealed record SavedView(float X, float Y, float Z, float Yaw, float Pitch, bool Flying,
    int Seed = World.DefaultSeed, int GeneratorVersion = 1, long IslandX = 0, long IslandZ = 0, int CoordinateVersion = 0,
    AdventureSave? Progress = null);
internal static class SaveStore
{
    internal const int CurrentGeneratorVersion = 3;
    public static string DirectoryPath
    {
        get
        {
#if DEBUG
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "CAERO.sln")))
                    return Path.Combine(dir.FullName, "SaveData");
            throw new DirectoryNotFoundException("DEBUG save path requires CAERO.sln above the executable.");
#else
            return AppContext.BaseDirectory;
#endif
        }
    }
    public static string? LastError { get; private set; }
    public static SavedView? Load(string? filePath = null)
    {
        try
        {
            string path = filePath ?? Path.Combine(DirectoryPath, "generated-view.json");
            if (!File.Exists(path)) return null;
            var view = JsonSerializer.Deserialize<SavedView>(File.ReadAllText(path));
            if (view == null || !float.IsFinite(view.X) || !float.IsFinite(view.Y) || !float.IsFinite(view.Z)
                || !float.IsFinite(view.Yaw) || !float.IsFinite(view.Pitch)
                || view.Y < -100 || view.Y > 1000 || view.CoordinateVersion is < 0 or > 1
                || view.GeneratorVersion is < 1 or > CurrentGeneratorVersion)
                return null;
            // Completed saves resume normal play; the current ending belongs only to this session.
            if (view.Progress is { Escaped: true } progress)
            {
                if (!AchievementStore.UnlockClear(filePath)) Console.Error.WriteLine("ACHIEVEMENTS: " + AchievementStore.LastError);
                view = view with { Progress = progress with { Escaped = false, ExitId = null } };
            }
            if (view.GeneratorVersion < CurrentGeneratorVersion)
            {
                // Keep the world identity, but never restore an old position inside new geometry.
                var spawn = Sector.SpawnView(view.Seed);
                Vector3 direction = Vector3.Normalize(spawn.Target - spawn.Position);
                return new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z,
                    MathF.Atan2(direction.X, -direction.Z), MathF.Asin(direction.Y), false,
                    view.Seed, CurrentGeneratorVersion, 0, 0, 1, view.Progress);
            }
            // Legacy v3 saves used absolute floats. Convert without changing the world or view.
            var address = new WorldPosition(view.CoordinateVersion == 0 ? 0 : view.IslandX,
                view.CoordinateVersion == 0 ? 0 : view.IslandZ, new(view.X, view.Y, view.Z)).Normalized();
            return view with { X = address.Local.X, Z = address.Local.Z, IslandX = address.IslandX,
                IslandZ = address.IslandZ, CoordinateVersion = 1 };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or OverflowException)
        { LastError = ex.Message; return null; }
    }
    internal static SavedView Capture(Explorer explorer, World world)
    {
        var address = world.AddressOf(explorer.Position);
        var p = address.Local;
        var progress = world.Progress.Snapshot();
        if (progress.Escaped) progress = progress with { Escaped = false, ExitId = null };
        return new(p.X, p.Y, p.Z, explorer.Yaw, explorer.Pitch, explorer.Flying, world.Seed,
            CurrentGeneratorVersion, address.IslandX, address.IslandZ, 1, progress);
    }
    public static bool Save(Explorer explorer, World world, string? filePath = null) =>
        Save(Capture(explorer, world) with { Progress = world.Progress.Snapshot() }, filePath);
    internal static bool Save(SavedView view, string? filePath = null)
    {
        try
        {
            // Persist completion before normalizing the adventure back to its resumable state.
            // Even a new game or deletion of generated-view.json must retain this flag.
            bool achievementSaved = true;
            if (view.Progress is { Escaped: true } progress)
            {
                achievementSaved = AchievementStore.UnlockClear(filePath);
                if (achievementSaved) view = view with { Progress = progress with { Escaped = false, ExitId = null } };
            }
            string path = filePath ?? Path.Combine(DirectoryPath, "generated-view.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(view, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
            LastError = achievementSaved ? null : AchievementStore.LastError; return achievementSaved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { LastError = ex.Message; return false; }
    }
}
