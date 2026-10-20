using System.Numerics;
using System.Buffers.Binary;
using Raylib_cs;

namespace Caero;

internal sealed class ConversationUi : IDisposable
{
    private readonly Font font;
    public ConversationUi()
    {
        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string? path = new[] { "YuGothR.ttc", "meiryo.ttc", "msgothic.ttc" }.Select(f => Path.Combine(fonts, f)).FirstOrDefault(File.Exists);
        if (path == null) throw new FileNotFoundException("A Japanese Windows font (Yu Gothic / Meiryo / MS Gothic) is required for dialogue.");
        int[] glyphs = Enumerable.Range(32, 95).Concat(Dialogue.FontCharacters.Select(c => (int)c)).Distinct().Order().ToArray();
        font = Raylib.LoadFontFromMemory(".ttf", FontFace(File.ReadAllBytes(path)), 40, glyphs, glyphs.Length);
        if (font.Texture.Id == 0 || font.Texture.Id == Raylib.GetFontDefault().Texture.Id || font.GlyphCount < glyphs.Length)
            throw new InvalidOperationException("Cannot load Japanese dialogue font: " + path);
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
    }
    // raylib's stb loader expects a standalone sfnt at offset zero. Windows
    // supplies Japanese fonts as TTC collections, whose table offsets are global.
    // Repack the first face in memory; no Windows font is copied into the project.
    internal static byte[] FontFace(byte[] source)
    {
        int U16(int p) => BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(p, 2));
        int U32(int p) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(p, 4)));
        if (!source.AsSpan(0, 4).SequenceEqual("ttcf"u8)) return source;
        int offset = U32(12), count = U16(offset + 4), header = 12 + count * 16;
        using var stream = new MemoryStream();
        stream.Write(source, offset, header);
        Span<byte> value = stackalloc byte[4];
        for (int i = 0; i < count; i++)
        {
            int record = offset + 12 + i * 16, start = U32(record + 8), length = U32(record + 12);
            int destination = checked((int)stream.Length);
            stream.Position = 12 + i * 16 + 8;
            BinaryPrimitives.WriteUInt32BigEndian(value, (uint)destination); stream.Write(value);
            stream.Position = destination; stream.Write(source, start, length);
            while (stream.Length % 4 != 0) stream.WriteByte(0);
        }
        byte[] result = stream.ToArray();
        int head = -1;
        for (int i = 0; i < count; i++)
            if (result.AsSpan(12 + i * 16, 4).SequenceEqual("head"u8))
                head = (int)BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(12 + i * 16 + 8, 4));
        if (head >= 0)
        {
            result.AsSpan(head + 8, 4).Clear(); uint sum = 0;
            for (int i = 0; i < result.Length; i += 4) sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(i, 4)));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(head + 8, 4), unchecked(0xb1b0afba - sum));
        }
        return result;
    }
    private void Text(string text, float x, float y, float size, Color color) => Raylib.DrawTextEx(font, text, new(x, y), size, 1, color);
    public void Draw(Conversation session, Resident? target)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        var ink = new Color(231, 229, 215, 255); var muted = new Color(162, 170, 167, 255);
        if (session.Speaker is not { } speaker)
        {
            if (target == null) return;
            float size = Math.Clamp(w / 48f, 16, 23);
            string prompt = "[E] 話しかける　" + target.Name;
            float width = Raylib.MeasureTextEx(font, prompt, size, 1).X;
            Raylib.DrawRectangle((int)((w - width) / 2 - 18), h - 180, (int)width + 36, 42, new Color(12, 19, 23, 220));
            Text(prompt, (w - width) / 2, h - 172, size, ink);
            return;
        }
        float padding = w < 800 ? 22 : 48, panelWidth = w - padding * 2;
        float sizeText = Math.Clamp(w / 48f, 17, 25), lineHeight = sizeText * 1.55f;
        float panelHeight = Math.Max(200, lineHeight * 4.8f), top = h - panelHeight - 26;
        Raylib.DrawRectangle((int)padding, (int)top, (int)panelWidth, (int)panelHeight, new Color(10, 17, 21, 239));
        Raylib.DrawRectangle((int)padding, (int)top, 3, (int)panelHeight, new Color(164, 157, 133, 255));
        Text(speaker.Name, padding + 24, top + 18, sizeText * .85f, muted);
        string text = session.Text[..session.Revealed];
        float x = padding + 24, y = top + 58;
        foreach (char c in text)
        {
            if (c == '\n') { x = padding + 24; y += lineHeight; continue; }
            string letter = c.ToString();
            float advance = Raylib.MeasureTextEx(font, letter, sizeText, 1).X + 1;
            if (x + advance > w - padding - 24) { x = padding + 24; y += lineHeight; }
            Text(letter, x, y, sizeText, ink); x += advance;
        }
        Text($"{session.Page + 1} / {Dialogue.Lines[speaker.Profile].Pages.Length}    [E / Space] {(session.Revealed < session.Text.Length ? "全文表示" : "次へ")}    [Esc] 閉じる",
            padding + 24, top + panelHeight - 35, sizeText * .7f, muted);
    }
    public void Dispose() => Raylib.UnloadFont(font);
    public void DrawSwallowed(float remaining)
    {
        if (remaining <= 0) return;
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float alpha = Math.Clamp(remaining / 1.2f, 0, 1);
        Raylib.DrawRectangle(0, 0, w, h, new Color(0, 0, 0, (int)(alpha * 255)));
        string message = "……気がつくと、最初の島に戻っていた。";
        float size = Math.Clamp(w / 50f, 17, 24), width = Raylib.MeasureTextEx(font, message, size, 1).X;
        Text(message, (w - width) / 2, h * .55f, size, new Color(194, 197, 187, (int)(alpha * 230)));
    }
}
