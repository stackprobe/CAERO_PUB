using System.Numerics;
using System.Buffers.Binary;
using Raylib_cs;

namespace Caero;

internal sealed class ConversationUi : IDisposable
{
    private readonly Font font;
    private readonly Texture2D endingImage;
    public ConversationUi()
    {
        string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string? path = new[] { "YuGothR.ttc", "meiryo.ttc", "msgothic.ttc" }.Select(f => Path.Combine(fonts, f)).FirstOrDefault(File.Exists);
        if (path == null) throw new FileNotFoundException("A Japanese Windows font (Yu Gothic / Meiryo / MS Gothic) is required for dialogue.");
        int[] glyphs = Enumerable.Range(32, 95).Concat(Dialogue.FontCharacters.Select(c => (int)c))
            .Concat(SpecialContent.FontCharacters.Select(c => (int)c))
            .Concat(GameMenus.Characters.Select(c => (int)c))
            .Concat(GameMenus.ClearAchievementText.Select(c => (int)c))
            .Concat((EndingSequence.Message + "タイトルに戻る").Select(c => (int)c))
            .Concat(WarpTravel.UiCharacters.Select(c => (int)c)).Distinct().Order().ToArray();
        font = Raylib.LoadFontFromMemory(".ttf", FontFace(File.ReadAllBytes(path)), 40, glyphs, glyphs.Length);
        if (font.Texture.Id == 0 || font.Texture.Id == Raylib.GetFontDefault().Texture.Id || font.GlyphCount < glyphs.Length)
            throw new InvalidOperationException("Cannot load Japanese dialogue font: " + path);
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
        endingImage = ContentAssets.LoadTexture("ending.png");
        if (!Raylib.IsTextureValid(endingImage)) throw new InvalidDataException("Cannot load CAERO ending image.");
        Raylib.SetTextureFilter(endingImage, TextureFilter.Bilinear);
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
    internal void Label(string text, float x, float y, float size, Color color) => Text(text, x, y, size, color);
    internal float TextWidth(string text, float size) => Raylib.MeasureTextEx(font, text, size, 1).X;
    public void Draw(Conversation session, Resident? target)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        var ink = new Color(231, 229, 215, 255); var muted = new Color(162, 170, 167, 255);
        if (!session.Active)
        {
            if (target == null) return;
            float size = Math.Clamp(w / 48f, 16, 23);
            const string prompt = "[E] 話しかける";
            // Blank full-width glyphs can have zero advance in the loaded font.
            float nameOffset = TextWidth(prompt, size) + size + 1;
            float width = nameOffset + TextWidth(target.Name, size);
            Raylib.DrawRectangle((int)((w - width) / 2 - 18), h - 180, (int)width + 36, 42, new Color(12, 19, 23, 220));
            Text(prompt, (w - width) / 2, h - 172, size, ink);
            Text(target.Name, (w - width) / 2 + nameOffset, h - 172, size, ink);
            return;
        }
        float padding = w < 800 ? 22 : 48, panelWidth = w - padding * 2;
        float sizeText = Math.Clamp(w / 48f, 17, 25), lineHeight = sizeText * 1.55f;
        float panelHeight = Math.Max(200, lineHeight * 4.8f) + (session.AwaitingChoice ? lineHeight : 0), top = h - panelHeight - 26;
        Raylib.DrawRectangle((int)padding, (int)top, (int)panelWidth, (int)panelHeight, new Color(10, 17, 21, 239));
        Raylib.DrawRectangle((int)padding, (int)top, 3, (int)panelHeight, new Color(164, 157, 133, 255));
        Text(session.Title, padding + 24, top + 18, sizeText * .85f, muted);
        float Advance(char c) => (c == '\u3000' ? sizeText : Raylib.MeasureTextEx(font, c.ToString(), sizeText, 1).X) + 1;
        var layout = TextLayout.Arrange(session.Text, panelWidth - 48, Advance);
        foreach (var glyph in layout)
        {
            if (glyph.Index >= session.Revealed) break;
            // Preserve the existing full-width space advance without drawing a missing glyph.
            if (glyph.Character != '\u3000')
                Text(glyph.Character.ToString(), padding + 24 + glyph.X,
                    top + 58 + glyph.Row * lineHeight, sizeText, ink);
        }
        if (session.AwaitingChoice)
        {
            float choiceY = top + panelHeight - 78;
            Text(session.SelectedYes ? "> はい" : "  はい", padding + 24, choiceY, sizeText, session.SelectedYes ? ink : muted);
            Text(session.SelectedYes ? "  いいえ" : "> いいえ", padding + 24 + sizeText * 7, choiceY, sizeText, session.SelectedYes ? muted : ink);
        }
        string controls = session.AwaitingChoice ? "[矢印 / WASD] 選択  [E / Space / Enter] 決定"
            : $"[E / Space] {(session.Revealed < session.Text.Length ? "全文表示" : "次へ")}";
        Text($"{session.Page + 1} / {session.PageCount}    {controls}    [Esc] 閉じる",
            padding + 24, top + panelHeight - 35, sizeText * .7f, muted);
    }
    public void Dispose() { Raylib.UnloadFont(font); Raylib.UnloadTexture(endingImage); }
    internal void DrawSpecial(SpecialObject? target)
    {
        if (target == null) return;
        string verb = target.Definition.Kind == SpecialKind.Item ? "取得する" : "調べる";
        string prompt = "[E] " + verb;
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float size = Math.Clamp(w / 48f, 16, 23);
        float nameOffset = TextWidth(prompt, size) + size + 1;
        float width = nameOffset + TextWidth(target.Definition.Name, size);
        Raylib.DrawRectangle((int)((w - width) / 2 - 18), h - 180, (int)width + 36, 42, new Color(12, 19, 23, 230));
        Text(prompt, (w - width) / 2, h - 172, size, target.Definition.Tint);
        Text(target.Definition.Name, (w - width) / 2 + nameOffset, h - 172, size, target.Definition.Tint);
    }
    private static Rectangle EndingButton()
    {
        float w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float width = Math.Min(340, w - 48), height = Math.Clamp(h / 14, 38, 56);
        return new((w - width) / 2, h - height - Math.Max(24, h * .06f), width, height);
    }
    internal static bool PollEndingReturn(EndingSequence ending)
    {
        if (!Raylib.IsWindowFocused()) return false;
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left)
            && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), EndingButton());
        bool confirm = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space);
        bool held = Raylib.IsMouseButtonDown(MouseButton.Left) || Raylib.IsKeyDown(KeyboardKey.Enter)
            || Raylib.IsKeyDown(KeyboardKey.Space);
        return ending.ReturnInput(click || confirm, held);
    }
    internal void DrawEnding(EndingSequence ending)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, w, h, new Color(255, 255, 255, (int)(ending.WhiteOpacity * 255)));
        if (ending.ImageOpacity > 0)
        {
            float scale = Math.Max(w / (float)endingImage.Width, h / (float)endingImage.Height);
            Raylib.DrawTexturePro(endingImage, new(0, 0, endingImage.Width, endingImage.Height),
                new((w - endingImage.Width * scale) / 2, (h - endingImage.Height * scale) / 2,
                    endingImage.Width * scale, endingImage.Height * scale), Vector2.Zero, 0,
                new Color(255, 255, 255, (int)(ending.ImageOpacity * 255)));
        }
        float size = Math.Clamp(w / 36f, 22, 40), width = TextWidth(EndingSequence.Message, size);
        Text(EndingSequence.Message, (w - width) / 2, (h - size) / 2, size,
            new Color(46, 53, 57, (int)(ending.TextOpacity * 255)));
        if (!ending.CanReturn) return;
        Rectangle button = EndingButton();
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), button);
        Raylib.DrawRectangleRec(button, hover ? new Color(49, 69, 80, 240) : new Color(15, 29, 40, 225));
        float buttonSize = Math.Clamp(w / 55f, 16, 25);
        Text("タイトルに戻る", (w - TextWidth("タイトルに戻る", buttonSize)) / 2,
            button.Y + (button.Height - buttonSize) / 2, buttonSize, new Color(240, 241, 233, 255));
    }
    public void DrawWarp(WarpTarget? target)
    {
        if (target == null) return;
        const string prompt = "[E] 調べる", name = "ワープ装置";
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float size = Math.Clamp(w / 48f, 16, 23);
        float nameOffset = TextWidth(prompt, size) + size + 1;
        float width = nameOffset + TextWidth(name, size);
        Raylib.DrawRectangle((int)((w - width) / 2 - 18), h - 180, (int)width + 36, 42, new Color(12, 25, 32, 230));
        Text(prompt, (w - width) / 2, h - 172, size, new Color(191, 222, 231, 255));
        Text(name, (w - width) / 2 + nameOffset, h - 172, size, new Color(191, 222, 231, 255));
    }
    internal const float SwallowedBlackDuration = 1, SwallowedMessageDuration = 3, SwallowedFadeDuration = 1.2f;
    internal const float SwallowedDuration = SwallowedBlackDuration + SwallowedMessageDuration + SwallowedFadeDuration;
    internal static float SwallowedOpacity(float remaining) => Math.Clamp(remaining / SwallowedFadeDuration, 0, 1);
    internal static bool SwallowedMessageVisible(float remaining)
        => remaining <= SwallowedMessageDuration + SwallowedFadeDuration && remaining > SwallowedFadeDuration;
    public void DrawSwallowed(float remaining)
    {
        if (remaining <= 0) return;
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float alpha = SwallowedOpacity(remaining);
        Raylib.DrawRectangle(0, 0, w, h, new Color(0, 0, 0, (int)(alpha * 255)));
        if (!SwallowedMessageVisible(remaining)) return;
        string message = "……気がつくと、最初の島に戻っていた。";
        float size = Math.Clamp(w / 50f, 17, 24), width = Raylib.MeasureTextEx(font, message, size, 1).X;
        Text(message, (w - width) / 2, (h - size) / 2, size, new Color(194, 197, 187, 230));
    }
}
