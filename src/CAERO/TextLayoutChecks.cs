namespace Caero;

internal static class TextLayoutChecks
{
    internal static void Run()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Text layout check failed: " + message); }
        string marker = TextLayout.ClippingMarker;
        string Shown(IReadOnlyList<TextLayout.Glyph> glyphs) => new(glyphs.Select(g => g.Character).ToArray());
        var clipped = TextLayout.Arrange("abcdefghijklmnopqrstuvwxyz", marker.Length + 7, _ => 1);
        Check(Shown(clipped) == "abcdefg" + marker && clipped.All(g => g.Row == 0),
            "cut inside a word, reserve marker width and never wrap the hidden remainder");
        var exact = TextLayout.Arrange("one two", 7, _ => 1);
        Check(Shown(exact) == "one two" && exact.All(g => g.Row == 0), "an exact fit needs no marker");
        var explicitLines = TextLayout.Arrange("one\n\ntwo", 20, _ => 1);
        Check(explicitLines.Single(g => g.Index == 5).Row == 2, "preserve authored blank lines");
        var following = TextLayout.Arrange(new string('x', 50) + "\nNext", marker.Length + 7, _ => 1);
        Check(Shown(following.Where(g => g.Row == 1).ToArray()) == "Next"
            && following.First(g => g.Row == 1).Index == 51 && following.First(g => g.Row == 1).X == 0,
            "a clipped line does not consume or displace the next authored line");
        var narrow = TextLayout.Arrange("WW", 5, c => c == 'W' ? 10 : 1);
        Check(Shown(narrow) == marker[..5] && narrow.All(g => g.X + 1 <= 5),
            "even a marker wider than the available area stays within its bounds");
        var proportional = TextLayout.Arrange("iiiWWWWWWWWWWWW", marker.Length + 6, c => c == 'W' ? 3 : 1);
        Check(Shown(proportional) == "iiiW" + marker, "cut according to proportional advances rather than character count");
        Check(Shown(clipped.Where(g => g.Index < 7).ToArray()) == "abcdefg"
            && Shown(clipped.Where(g => g.Index < 8).ToArray()) == "abcdefg" + marker,
            "the marker appears at the first clipped source character without moving visible text");
        var spaces = TextLayout.Arrange("  A B  ", 7, _ => 1);
        Check(Shown(spaces) == "  A B  ", "retain authored whitespace on lines that fit");
        Check(TextLayout.Arrange("text", 0, _ => 1).Count == 0
            && TextLayout.Arrange("", 20, _ => 1).Count == 0, "empty or zero-width areas are safe");
        var japanese = TextLayout.Arrange(new string('あ', 40) + "\n　次の行", marker.Length + 7, _ => 1);
        Check(Shown(japanese.Where(g => g.Row == 0).ToArray()) == new string('あ', 7) + marker
            && Shown(japanese.Where(g => g.Row == 1).ToArray()) == "　次の行",
            "Japanese text clips without wrapping and preserves a full-width leading space on the next line");
        Console.WriteLine("TEXT LAYOUT: clipping, marker width, proportional advances, explicit newlines, exact fits and reveal stability passed.");
    }
}
