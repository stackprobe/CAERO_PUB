using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace Caero;

// A flush floor inlay, readable while facing the destination island.
internal sealed class DestinationPanel
{
    internal BigInteger Number { get; }
    internal Vector3 Center { get; }
    internal Vector3 Forward { get; }
    internal float Width { get; }
    internal float Height { get; }
    internal float DigitHeight { get; }
    internal Geometry Footprint { get; } = new();
    internal Geometry Surface { get; } = new();
    internal DestinationPanel(BigInteger number, Vector3 center, Vector3 forward, byte glyphTag = 250, bool prime = false,
        float maxWidth = 3.6f, float maxHeight = 3.6f, bool underline = false, int singleLineDigits = 0)
    {
        Number = number; Center = center; Forward = Vector3.Normalize(forward);
        string text = number.ToString(CultureInfo.InvariantCulture);
        // Keep even full 64-bit island addresses readable: wrap very long numbers
        // instead of shrinking a single line without limit.
        int lineLimit = Math.Min(8, (int)((maxWidth / .1f - 3) / 4));
        bool keepSingleLine = text.Length <= singleLineDigits;
        if (keepSingleLine) lineLimit = Math.Max(lineLimit, text.Length);
        int rows = (text.Length + lineLimit - 1) / lineLimit, columns = (text.Length + rows - 1) / rows;
        int width = Math.Max(9, columns * 4 + 3), height = rows * 6 + 3 + (underline ? 2 : 0);
        float cell = Math.Min(text.Length == 1 ? 2f / 9 : text.Length == 2 ? .20f : text.Length == 3 ? .18f : .17f,
            Math.Min(maxWidth / width, maxHeight / height));
        // Narrow sky bridges can use slightly condensed glyphs for short
        // numbers: preserve readable height while fitting one line inside rails.
        float horizontalCell = cell;
        if (keepSingleLine) cell = Math.Min(maxHeight / height, Math.Max(.1f, cell));
        Width = width * horizontalCell; Height = height * cell; DigitHeight = 5 * cell;
        var pixels = new int[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) pixels[x, y] = 1;
        for (int row = 0; row < rows; row++)
        {
            string line = text.Substring(row * columns, Math.Min(columns, text.Length - row * columns));
            Sector.Stamp(pixels, line, (width - (line.Length * 4 - 1)) / 2, 2 + row * 6, 2);
        }
        if (underline)
            for (int x = 2; x < width - 2; x++) pixels[x, height - 3] = 2;
        Color[] palette = [new(155, 150, 131, 250), new(115, 109, 92, 250),
            prime ? new(145, 46, 66, (int)glyphTag) : new(76, 72, 61, (int)glyphTag)];
        Vector3 right = Vector3.Cross(Forward, Vector3.UnitY);
        void Rect(Geometry g, int x0, int y0, int x1, int y1, Color color)
        {
            Vector3 P(int x, int y) => Center + right * ((x - width * .5f) * horizontalCell)
                + Forward * ((height * .5f - y) * cell);
            g.Quad(P(x0, y1), P(x1, y1), P(x1, y0), P(x0, y0), color);
        }
        Rect(Footprint, 0, 0, width, height, palette[0]);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width;)
            {
                int start = x, shade = pixels[x++, y];
                while (x < width && pixels[x, y] == shade) x++;
                Rect(Surface, start, y, x, y + 1, palette[shade]);
            }
    }
}
