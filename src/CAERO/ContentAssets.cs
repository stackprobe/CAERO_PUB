using System.Text;
using Raylib_cs;

namespace Caero;

internal static class ContentAssets
{
    private static readonly string LooseDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>?> Package = new(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Content.dat");
        return File.Exists(path) ? ContentArchive.Decode(File.ReadAllBytes(path)) : null;
    });

    internal static byte[] ReadBytes(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(LooseDirectory, fullPath).Replace('\\', '/');
        // Explicit fixture paths outside Content keep working in validation tools.
        if (relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(relative)
            && Package.Value is { } package)
            return package.TryGetValue(relative, out var bytes) ? bytes
                : throw new FileNotFoundException("Asset missing from Content.dat: " + relative);
        return File.ReadAllBytes(fullPath);
    }

    internal static string ReadText(string path)
    {
        using var reader = new StreamReader(new MemoryStream(ReadBytes(path)), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    internal static Image LoadImage(string name)
        => Raylib.LoadImageFromMemory(Path.GetExtension(name), ReadBytes(Path.Combine(LooseDirectory, name)));

    internal static Texture2D LoadTexture(string name)
    {
        var image = LoadImage(name);
        try { return Raylib.LoadTextureFromImage(image); }
        finally { Raylib.UnloadImage(image); }
    }
}
