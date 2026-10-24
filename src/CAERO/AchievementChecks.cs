using System.Text.Json;

namespace Caero;

internal static class AchievementChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Achievement check failed: " + message); }
    internal static void Run()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "achievement-check-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "adventure.json"), legacy = Path.Combine(folder, "legacy.json");
        var start = new SavedView(0, 4, 0, 0, 0, false, GeneratorVersion: SaveStore.CurrentGeneratorVersion,
            CoordinateVersion: 1, Progress: new([]));
        try
        {
            Check(!AchievementStore.IsCleared(path), "missing achievement starts locked");
            Check(SaveStore.Save(start, path) && !AchievementStore.IsCleared(path), "normal save does not grant completion");
            var completed = start with { Progress = new([], true, "exit") };
            Check(SaveStore.Save(completed, path) && AchievementStore.IsCleared(path), "goal save immediately persists achievement");
            Check(JsonSerializer.Deserialize<SavedView>(File.ReadAllText(path))?.Progress is { Escaped: false, ExitId: null },
                "adventure resumes before the ending while lifetime completion remains");
            Check(SaveStore.Save(start with { Seed = 492 }, path) && AchievementStore.IsCleared(path), "new game and seed preserve completion");
            File.Delete(path);
            Check(SaveStore.Load(path) == null && AchievementStore.IsCleared(path), "deleting adventure does not delete achievement");
            Check(!AchievementStore.IsCleared(legacy), "test saves have isolated achievement stores");
            File.WriteAllText(legacy, JsonSerializer.Serialize(completed));
            Check(SaveStore.Load(legacy)?.Progress is { Escaped: false } && AchievementStore.IsCleared(legacy),
                "older completed save migrates without replaying ending");
            string recorded = File.ReadAllText(AchievementStore.PathFor(path));
            Check(AchievementStore.UnlockClear(path) && File.ReadAllText(AchievementStore.PathFor(path)) == recorded,
                "repeated completion preserves an existing flag");
            using var world = new World(upload: false);
            var player = new Explorer(); player.GoTo(world.Views[0]);
            var auto = new AutoSave(legacy);
            // A live escape feeds the same autosave path used by BeginEnding, without a graphics device.
            world.Progress.Escape("exit");
            File.Delete(AchievementStore.PathFor(legacy));
            Check(auto.Flush(player, world) && AchievementStore.IsCleared(legacy), "ending autosave grants completion");
            Console.WriteLine("ACHIEVEMENTS: goal autosave, independent persistence, new game, deleted save, legacy migration and repeat clear passed.");
        }
        finally
        {
            foreach (string save in new[] { path, legacy })
                foreach (string file in new[] { save, save + ".tmp", AchievementStore.PathFor(save), AchievementStore.PathFor(save) + ".tmp" })
                    if (File.Exists(file)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
