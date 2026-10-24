namespace Caero;

internal static class BgmAssets
{
    internal static readonly string[] Names = ["day", "night", "title"];
    internal const float Duration = 300;
    internal static string PathFor(string name)
    {
        if (!Names.Contains(name)) throw new ArgumentOutOfRangeException(nameof(name));
        return Path.Combine(AppContext.BaseDirectory, "Music", name + ".mp3");
    }
}
