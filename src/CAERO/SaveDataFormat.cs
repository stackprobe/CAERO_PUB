using System.Text.Json;

namespace Caero;

// Lightweight obfuscation for local saves, not encryption.
internal static class SaveDataFormat
{
    private const string Header = "CAERO-SAVE:1\n";

    internal static string Serialize<T>(T value) =>
#if DEBUG
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
#else
        Header + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value));
#endif

    internal static T? Deserialize<T>(string data)
    {
        if (data.StartsWith(Header, StringComparison.Ordinal))
            return JsonSerializer.Deserialize<T>(Convert.FromBase64String(data[Header.Length..]));

        // Both build configurations can read plain JSON and obfuscated saves.
        return JsonSerializer.Deserialize<T>(data);
    }
}
