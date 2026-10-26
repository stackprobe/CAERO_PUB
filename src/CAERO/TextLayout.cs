namespace Caero;

// Lay out the complete page before applying the typewriter reveal. Only authored
// newlines create rows; overflowing lines end in a marker instead of wrapping.
internal static class TextLayout
{
    internal const string ClippingMarker = "... [clipped]";
    internal readonly record struct Glyph(int Index, char Character, float X, int Row);

    internal static IReadOnlyList<Glyph> Arrange(string text, float width, Func<char, float> advance)
    {
        var result = new List<Glyph>(text.Length);
        float x = 0;
        int row = 0, lineStart = 0;
        float markerWidth = ClippingMarker.Sum(c => advance(c));
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '\n') { x = 0; row++; lineStart = result.Count; i++; continue; }
            float step = advance(text[i]);
            if (x + step <= width)
            {
                result.Add(new(i, text[i], x, row));
                x += step;
                i++;
                continue;
            }
            // This line overflows. Reclaim just enough of its prefix to fit the
            // marker, retaining the original index for typewriter reveal timing.
            int clippedIndex = i;
            while (result.Count > lineStart && x + markerWidth > width)
            {
                var last = result[^1];
                x = last.X; clippedIndex = last.Index;
                result.RemoveAt(result.Count - 1);
            }
            foreach (char c in ClippingMarker)
            {
                step = advance(c);
                // Extremely narrow areas show only the marker characters that fit.
                if (x + step > width) break;
                result.Add(new(clippedIndex, c, x, row));
                x += step;
            }
            while (i < text.Length && text[i] != '\n') i++;
        }
        return result;
    }
}
