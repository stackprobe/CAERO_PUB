using System.Text.Json;

namespace Caero;

internal sealed record Achievements(bool Cleared = false);

// Lifetime achievements are separate from the replaceable adventure save.
internal static class AchievementStore
{
    internal static string PathFor(string? savePath = null) => savePath == null
        ? Path.Combine(SaveStore.DirectoryPath, "achievements.json")
        : Path.GetFullPath(savePath) + ".achievements.json";
    internal static string? LastError { get; private set; }
    internal static bool IsCleared(string? savePath = null)
    {
        try
        {
            string path = PathFor(savePath);
            return File.Exists(path) && JsonSerializer.Deserialize<Achievements>(File.ReadAllText(path))?.Cleared == true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { LastError = error.Message; return false; }
    }
    internal static bool UnlockClear(string? savePath = null)
    {
        if (IsCleared(savePath)) return true;
        try
        {
            string path = PathFor(savePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Achievements(true),
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
            LastError = null; return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { LastError = error.Message; return false; }
    }
}
