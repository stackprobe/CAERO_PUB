using System.Text.Json;
using System.Text.Json.Nodes;

namespace Caero;

internal static class DialogueChecks
{
    public static void Run()
    {
        TextLayoutChecks.Run();
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Dialogue check failed: " + message); }
        var lines = Dialogue.Lines;
        Check(lines.Length == 20 && lines.Sum(s => s.Pages.Length) == 60, "20 conversations and 60 pages");
        Check(lines.Select(s => s.Id).SequenceEqual(Enumerable.Range(0, 20)), "stable contiguous profile IDs");
        Check(lines.SelectMany(s => s.Pages).Distinct().Count() == 60, "no repeated pages");
        Check(lines.Select(s => s.Theme).Distinct().Count() == 20 && lines.GroupBy(s => s.Pose).All(g => g.Count() >= 6),
            "20 distinct subjects distributed across all three poses");
        Check(lines.Select(s => (s.Age, s.Gender)).Distinct().Count() == 6
            && lines.Select(s => s.Pose).Distinct().Count() == 3, "both genders, three ages and three poses");
        Check(lines[0].Age == ResidentAge.Elder && lines[5].Age == ResidentAge.Child
            && lines[7].Age == ResidentAge.Adult && lines[0].Pose == ResidentPose.Sitting
            && lines[5].Pose == ResidentPose.Walking && lines[7].Pose == ResidentPose.Standing,
            "existing profiles retain ages with sitting, walking and standing poses");
        var reload = Dialogue.Load(Dialogue.FilePath);
        Check(lines.Zip(reload).All(p => p.First.Id == p.Second.Id && p.First.Name == p.Second.Name
            && p.First.Age == p.Second.Age && p.First.Gender == p.Second.Gender && p.First.Pose == p.Second.Pose
            && p.First.Theme == p.Second.Theme && p.First.Pages.SequenceEqual(p.Second.Pages)), "external file reproduces the complete catalogue");
        string fixture = Path.Combine(AppContext.BaseDirectory, "caero-dialogue-" + Guid.NewGuid().ToString("N") + ".json");
        var source = ContentAssets.ReadText(Dialogue.FilePath);
        void Reject(Action<JsonObject> edit, string description)
        {
            var root = JsonNode.Parse(source)!.AsObject(); edit(root);
            File.WriteAllText(fixture, root.ToJsonString());
            bool rejected = false;
            try { Dialogue.Load(fixture); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { rejected = true; }
            Check(rejected, description);
        }
        try
        {
            Reject(root => root["conversations"]![7]!["id"] = 6, "duplicate/noncontiguous ID rejected");
            Reject(root => root["conversations"]![7]!["pages"] = new JsonArray("only one page"), "wrong page count rejected");
            Reject(root => root["conversations"]![7]!["pages"]![0] = " ", "empty text rejected");
            Reject(root => root["conversations"]![7]!["age"] = "unknown", "unknown age rejected");
            Reject(root => root["conversations"]![7]!.AsObject().Remove("pose"), "missing required metadata rejected");
            Reject(root => root["conversations"]![7]!["pages"] = root["conversations"]![0]!["pages"]!.DeepClone(), "duplicate text rejected");
            Reject(root => root["version"] = 99, "unsupported schema rejected");
            File.WriteAllText(fixture, "{");
            bool malformed = false;
            try { Dialogue.Load(fixture); } catch (JsonException) { malformed = true; }
            Check(malformed, "malformed JSON rejected");
        }
        finally { if (File.Exists(fixture)) File.Delete(fixture); }
        Console.WriteLine($"DIALOGUE: {lines.Length} external conversations, {lines.Sum(s => s.Pages.Length)} pages, {lines.Select(s => s.Theme).Distinct().Count()} themes, metadata, stable IDs and invalid-file diagnostics passed.");
    }
}
