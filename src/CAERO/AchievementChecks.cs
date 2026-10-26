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
            var inventory = start with { IslandX = long.MaxValue - 1, IslandZ = long.MinValue + 1,
                Progress = new(["item-1", "薬瓶"]) };
            Check(SaveStore.Save(inventory, path), "inventory save succeeds");
            string encoded = File.ReadAllText(path);
#if DEBUG
            Check(encoded.Contains("\"Collected\"") && encoded.Contains('\n'),
                "DEBUG save uses indented plain JSON");
            var decoded = JsonSerializer.Deserialize<SavedView>(encoded);
#else
            Check(encoded.StartsWith("CAERO-SAVE:1\n") && !encoded.Contains("Collected"),
                "save uses a versioned header and hides JSON field names");
            var decoded = JsonSerializer.Deserialize<SavedView>(Convert.FromBase64String(encoded[(encoded.IndexOf('\n') + 1)..]));
#endif
            var restored = SaveStore.Load(path);
            Check(decoded?.IslandX == inventory.IslandX && restored?.IslandX == inventory.IslandX
                && restored.IslandZ == inventory.IslandZ
                && restored.Progress!.Collected.SequenceEqual(inventory.Progress!.Collected),
                "save restores huge coordinates and Unicode inventory");
            File.WriteAllText(legacy, "CAERO-SAVE:1\n" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(inventory)));
            Check(SaveStore.Load(legacy)?.Progress?.Collected.SequenceEqual(inventory.Progress!.Collected) == true,
                "both configurations can read encoded saves");
            File.WriteAllText(AchievementStore.PathFor(legacy),
                "CAERO-SAVE:1\n" + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Achievements(true))));
            Check(AchievementStore.IsCleared(legacy), "both configurations can read encoded clear data");
            File.Delete(AchievementStore.PathFor(legacy));
            var completed = start with { Progress = new([], true, "exit") };
            Check(SaveStore.Save(completed, path) && AchievementStore.IsCleared(path), "goal save immediately persists achievement");
            Check(SaveDataFormat.Deserialize<SavedView>(File.ReadAllText(path))?.Progress is { Escaped: false, ExitId: null },
                "adventure resumes before the ending while lifetime completion remains");
            Check(SaveStore.Save(start with { Seed = 492 }, path) && AchievementStore.IsCleared(path), "new game and seed preserve completion");
            File.Delete(path);
            Check(SaveStore.Load(path) == null && AchievementStore.IsCleared(path), "deleting adventure does not delete achievement");
            Check(!AchievementStore.IsCleared(legacy), "test saves have isolated achievement stores");
            File.WriteAllText(legacy, JsonSerializer.Serialize(completed));
            Check(SaveStore.Load(legacy)?.Progress is { Escaped: false } && AchievementStore.IsCleared(legacy),
                "older completed save migrates without replaying ending");
            string recorded = File.ReadAllText(AchievementStore.PathFor(path));
#if DEBUG
            Check(JsonSerializer.Deserialize<Achievements>(recorded)?.Cleared == true && recorded.Contains('\n'),
                "DEBUG clear data also uses indented plain JSON");
#else
            Check(recorded.StartsWith("CAERO-SAVE:1\n") && !recorded.Contains("Cleared"),
                "clear data also uses the encoded format");
#endif
            Check(AchievementStore.UnlockClear(path) && File.ReadAllText(AchievementStore.PathFor(path)) == recorded,
                "repeated completion preserves an existing flag");
            File.WriteAllText(AchievementStore.PathFor(legacy), JsonSerializer.Serialize(new Achievements(true)));
            Check(AchievementStore.IsCleared(legacy), "legacy JSON clear data remains readable");
            foreach (string invalid in new[] { "CAERO-SAVE:1\n!invalid!", "CAERO-SAVE:1\n",
                "CAERO-SAVE:1\nbm90IGpzb24=", "CAERO-SAVE:2\ne30=", "invalid-json" })
            {
                File.WriteAllText(legacy, invalid);
                File.WriteAllText(AchievementStore.PathFor(legacy), invalid);
                Check(SaveStore.Load(legacy) == null && !AchievementStore.IsCleared(legacy),
                    "corrupt or unsupported data is rejected without crashing");
            }
            using var world = new World(upload: false);
            var player = new Explorer(); player.GoTo(world.Views[0]);
            var auto = new AutoSave(legacy);
            // A live escape feeds the same autosave path used by BeginEnding, without a graphics device.
            world.Progress.Escape("exit");
            File.Delete(AchievementStore.PathFor(legacy));
            Check(auto.Flush(player, world) && AchievementStore.IsCleared(legacy), "ending autosave grants completion");
            Console.WriteLine("ACHIEVEMENTS: build-specific save format, both input formats, corrupt data, goal autosave, independent persistence, new game, deleted save, legacy migration and repeat clear passed.");
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
