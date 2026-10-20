using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caero;

internal static class Dialogue
{
    internal sealed record Speech
    {
        public required int Id { get; init; }
        public required string Name { get; init; }
        public required ResidentAge Age { get; init; }
        public required string Gender { get; init; }
        public required ResidentPose Pose { get; init; }
        public required string Theme { get; init; }
        public required string[] Pages { get; init; }
    }
    private sealed record Catalogue
    {
        public required int Version { get; init; }
        public required string Language { get; init; }
        public required Speech[] Conversations { get; init; }
    }
    internal static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "Content", "dialogue.ja.json");
    // The CLR initializes this once, safely shared by island workers and the UI.
    public static readonly Speech[] Lines = Load(FilePath);
    public static readonly string FontCharacters = string.Concat(Lines.Select(s => s.Name + string.Concat(s.Pages)))
        + "話しかける次へ閉じる全文表示白い衣の人々声はかすれ言葉が途切れる……［］　……気がつくと、最初の島に戻っていた。";

    internal static Speech[] Load(string path)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        using var stream = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<Catalogue>(stream, options)
            ?? throw new InvalidDataException($"Empty dialogue catalogue: {path}");
        if (data.Version != 1 || data.Language != "ja" || data.Conversations is not { Length: >= 8 })
            throw new InvalidDataException($"Unsupported dialogue catalogue version/language/count: {path}");
        var pages = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < data.Conversations.Length; i++)
        {
            var s = data.Conversations[i];
            if (s == null || s.Id != i || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 24
                || !Enum.IsDefined(s.Age) || !Enum.IsDefined(s.Pose) || s.Gender is not ("male" or "female")
                || string.IsNullOrWhiteSpace(s.Theme) || s.Pages is not { Length: 3 }
                || s.Pages.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 180 || p.Any(c => char.IsControl(c) && c != '\n')))
                throw new InvalidDataException($"Invalid dialogue entry {i} in {path}; check id, metadata and three nonempty pages.");
            if (!pages.Add(string.Join('\u001f', s.Pages)))
                throw new InvalidDataException($"Duplicate dialogue at entry {i} in {path}.");
        }
        return data.Conversations;
    }
}
