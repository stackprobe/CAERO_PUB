using System.Numerics;
using Raylib_cs;

namespace Caero;

internal enum GameScreen { Title, Playing, Paused, Controls }
internal enum MenuAction { None, NewGame, Continue, Exit, Resume, Controls, Title }

internal sealed class GameFlow
{
    internal GameScreen Screen { get; private set; } = GameScreen.Title;
    internal int Selected { get; private set; }
    private GameScreen controlsFrom = GameScreen.Title;
    private int controlsSelection;
    internal bool ControlsFromGame => Screen == GameScreen.Controls && controlsFrom == GameScreen.Paused;
    private int EntryCount => Screen == GameScreen.Title ? 4 : 3;
    internal void Start() { Screen = GameScreen.Playing; Selected = 0; }
    internal void Pause() { if (Screen == GameScreen.Playing) { Screen = GameScreen.Paused; Selected = 0; } }
    internal void ToTitle(bool canContinue = false) { Screen = GameScreen.Title; Selected = canContinue ? 1 : 0; }
    internal void ShowControls() { controlsFrom = Screen; controlsSelection = Selected; Screen = GameScreen.Controls; Selected = 0; }
    internal void Back() { Screen = controlsFrom; Selected = controlsSelection; }
    internal void Navigate(int delta, bool canContinue)
    {
        if (Screen is not (GameScreen.Title or GameScreen.Paused)) return;
        Selected = (Selected + delta + EntryCount) % EntryCount;
        if (Screen == GameScreen.Title && Selected == 1 && !canContinue) Selected = (Selected + delta + EntryCount) % EntryCount;
    }
    internal void Select(int index, bool canContinue)
    {
        if (index >= 0 && index < EntryCount && !(Screen == GameScreen.Title && index == 1 && !canContinue)) Selected = index;
    }
    internal MenuAction Confirm(bool canContinue) => Screen switch
    {
        GameScreen.Title => Selected switch { 0 => MenuAction.NewGame, 1 when canContinue => MenuAction.Continue, 2 => MenuAction.Controls, 3 => MenuAction.Exit, _ => MenuAction.None },
        GameScreen.Paused => Selected switch { 0 => MenuAction.Resume, 1 => MenuAction.Controls, _ => MenuAction.Title },
        GameScreen.Controls => MenuAction.Resume,
        _ => MenuAction.None
    };
}

internal sealed class GameMenus : IDisposable
{
    private readonly ConversationUi text;
    private readonly Texture2D title;
    internal GameMenus(ConversationUi text)
    {
        this.text = text;
        title = ContentAssets.LoadTexture("title.png");
        if (!Raylib.IsTextureValid(title)) throw new InvalidDataException("Cannot load CAERO title image.");
        Raylib.SetTextureFilter(title, TextureFilter.Bilinear);
    }
    public void Dispose() => Raylib.UnloadTexture(title);
    private const string SaveLossWarning = "現在のセーブデータは失われます。";
    internal const string Characters = "はじめから（ゲームスタート）コンテニュー終了ポーズゲームを続ける操作方法タイトルへもどる［Enter / Esc］戻る移動視点・飛行方向高速移動ジャンプ・遊泳上昇飛行薬取得後、ジャンプ中に開始 / 解除話す・調べる / 全文表示・次ページ本文を閉じる / マウス解放（デスクトップや他のアプリを操作したいときに）ポーズ・決定全画面切替？？？" + SaveLossWarning;
    internal static string FlightDescription(bool canFly) => canFly ? "飛行薬取得後、ジャンプ中に開始 / 解除" : "？？？";
    private static readonly Color Ink = new(232, 234, 222, 255), Muted = new(139, 158, 162, 255);
    private static string[] Entries(GameScreen screen) => screen == GameScreen.Title
        ? ["はじめから（ゲームスタート）", "コンテニュー", "操作方法", "終了"]
        : ["ゲームを続ける", "操作方法", "タイトルへもどる"];
    private static Rectangle Button(GameScreen screen, int index)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        float width = Math.Min(460, w - 48), height = screen == GameScreen.Title ? Math.Clamp(h / 15f, 30, 56) : Math.Clamp(h / 13f, 36, 58);
        float gap = screen == GameScreen.Title ? Math.Clamp(h * .014f, 6, 12) : 14;
        float top = h * (screen == GameScreen.Title ? .55f : .43f);
        if (screen == GameScreen.Title)
            top = Math.Min(top + height + gap, h - 16 - (4 * height + 3 * gap));
        return new((w - width) / 2, top + index * (height + gap), width, height);
    }
    private void Center(string value, float y, float size, Color color)
        => text.Label(value, (Raylib.GetScreenWidth() - text.TextWidth(value, size)) / 2, y, size, color);
    internal MenuAction Input(GameFlow flow, bool canContinue)
    {
        if (flow.Screen == GameScreen.Controls)
            return Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Escape)
                || Raylib.IsMouseButtonPressed(MouseButton.Left) ? MenuAction.Resume : MenuAction.None;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) flow.Navigate(-1, canContinue);
        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) flow.Navigate(1, canContinue);
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);
        if (Raylib.GetMouseDelta().LengthSquared() > 0 || click)
            for (int i = 0; i < Entries(flow.Screen).Length; i++)
                if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Button(flow.Screen, i)))
                {
                    if (flow.Screen == GameScreen.Title && i == 1 && !canContinue) return MenuAction.None;
                    flow.Select(i, canContinue); if (click) return flow.Confirm(canContinue);
                }
        if (flow.Screen == GameScreen.Paused && Raylib.IsKeyPressed(KeyboardKey.Escape)) return MenuAction.Resume;
        return Raylib.IsKeyPressed(KeyboardKey.Enter) ? flow.Confirm(canContinue) : MenuAction.None;
    }
    internal void Draw(GameFlow flow, bool canContinue, bool canFly = false)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        if (flow.Screen == GameScreen.Title)
        {
            Raylib.ClearBackground(new(15, 25, 32, 255));
            // Center-crop on resize; the original 1440x900 composition stays undistorted.
            float scale = Math.Max(w / (float)title.Width, h / (float)title.Height);
            Raylib.DrawTexturePro(title, new(0, 0, title.Width, title.Height),
                new((w - title.Width * scale) / 2, (h - title.Height * scale) / 2, title.Width * scale, title.Height * scale), Vector2.Zero, 0, Color.White);
        }
        else Raylib.DrawRectangle(0, 0, w, h, new Color(7, 14, 20, flow.Screen == GameScreen.Controls ? 255 : 218));
        if (flow.Screen == GameScreen.Controls) { DrawControls(flow.ControlsFromGame && canFly); return; }
        if (flow.Screen == GameScreen.Paused) Center("ポーズ", h * .24f, Math.Clamp(w / 45f, 22, 34), Ink);
        string[] entries = Entries(flow.Screen);
        for (int i = 0; i < entries.Length; i++)
        {
            Rectangle rect = Button(flow.Screen, i); bool disabled = flow.Screen == GameScreen.Title && i == 1 && !canContinue;
            bool selected = flow.Selected == i && !disabled;
            Raylib.DrawRectangleRec(rect, selected ? new(70, 92, 96, 235) : new Color(14, 26, 33, 190));
            if (selected) Raylib.DrawRectangle((int)rect.X, (int)rect.Y, 3, (int)rect.Height, new(160, 213, 214, 255));
            float size = Math.Clamp(w / 55f, 15, 25);
            Center(entries[i], rect.Y + (rect.Height - size) / 2, size, disabled ? new Color(76, 94, 99, 255) : Ink);
        }
        if (flow.Screen == GameScreen.Title && canContinue && flow.Selected == 0) DrawSaveLossWarning();
    }
    private void DrawSaveLossWarning()
    {
        Rectangle button = Button(GameScreen.Title, 0);
        float size = Math.Clamp(Raylib.GetScreenWidth() / 65f, 14, 20);
        float width = text.TextWidth(SaveLossWarning, size) + 32, height = size + 24;
        float center = Raylib.GetScreenWidth() / 2f, bottom = button.Y - 12;
        var fill = new Color(20, 32, 40, 242);
        Raylib.DrawRectangleRounded(new(center - width / 2, bottom - height, width, height), .18f, 8, fill);
        Raylib.DrawTriangle(new(center - 8, bottom - 1), new(center, bottom + 8), new(center + 8, bottom - 1), fill);
        Center(SaveLossWarning, bottom - height + 12, size, new Color(246, 221, 177, 255));
    }
    private void DrawControls(bool canFly)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Center("操作方法", h * .12f, Math.Clamp(w / 46f, 21, 32), Ink);
        (string Key, string Action)[] rows = [("WASD", "移動"), ("Mouse", "視点"), ("Shift", "高速移動"),
            ("Space", "ジャンプ・遊泳上昇"), ("F", FlightDescription(canFly)),
            ("E / Space", "話す・調べる / 全文表示・次ページ"), ("Esc", "本文を閉じる / マウス解放（デスクトップや他のアプリを操作したいときに）"),
            ("Enter", "ポーズ・決定"), ("Alt + Enter", "全画面切替")];
        float size = Math.Clamp(Math.Min(w / 65f, h / 32f), 14, 22), row = h * .058f;
        float x = Math.Max(24, w * .16f);
        for (int i = 0; i < rows.Length; i++)
        {
            text.Label(rows[i].Key, x, h * .24f + i * row, size, Muted);
            text.Label(rows[i].Action, w * .37f, h * .24f + i * row, size, Ink);
        }
        Center("［Enter / Esc］戻る", h * .89f, size, Muted);
    }
    internal static float AbilityOpacity(ItemEffect effect, bool flying, double seconds)
        => effect == ItemEffect.Flight && flying ? .25f + .75f * (.5f + .5f * (float)Math.Cos(seconds * Math.Tau / 1.2)) : 1;
    internal static void DrawAbilities(AdventureProgress progress, bool flying = false, double? animationTime = null)
    {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight(), slot = 0;
        foreach (var effect in new[] { ItemEffect.Flight, ItemEffect.Jump, ItemEffect.Dash })
        {
            int count = progress.AbilityCount(effect); if (count == 0) continue;
            float size = Math.Clamp(w / 24f, 38, 56), x = w - 24 - size - slot++ * (size + 12), y = h - 24 - size;
            Color color = effect == ItemEffect.Dash ? new(72, 194, 255, 255) : effect == ItemEffect.Jump ? new(255, 97, 110, 255) : new(104, 242, 157, 255);
            float opacity = AbilityOpacity(effect, flying, animationTime ?? Raylib.GetTime());
            color.A = (byte)(opacity * 255);
            Raylib.DrawRectangle((int)x, (int)y, (int)size, (int)size, new Color(10, 23, 29, (int)(200 * opacity)));
            Vector2 P(float u, float v) => new(x + u * size, y + v * size);
            void Line(float a, float b, float c, float d) => Raylib.DrawLineEx(P(a, b), P(c, d), Math.Max(2, size * .055f), color);
            if (effect == ItemEffect.Dash)
            {
                Line(.47f, .25f, .47f, .60f); Line(.47f, .60f, .77f, .67f); Line(.77f, .67f, .77f, .76f);
                Line(.30f, .76f, .77f, .76f); Line(.30f, .76f, .30f, .58f); Line(.30f, .58f, .30f, .25f);
                Line(.09f, .40f, .23f, .40f); Line(.06f, .54f, .21f, .54f);
            }
            else if (effect == ItemEffect.Jump)
            {
                Line(.50f, .74f, .50f, .23f); Line(.24f, .47f, .50f, .23f); Line(.76f, .47f, .50f, .23f);
                Line(.22f, .79f, .78f, .79f);
            }
            else
            {
                Line(.5f, .72f, .5f, .40f);
                for (int i = 0; i < 3; i++)
                {
                    float v = .25f + i * .14f;
                    Line(.5f, .62f, .13f + i * .08f, v); Line(.5f, .62f, .87f - i * .08f, v);
                }
            }
            for (int i = 0; i < Math.Min(count, 5); i++)
                Raylib.DrawCircleV(P(.5f + (i - (Math.Min(count, 5) - 1) / 2f) * .15f, .91f), Math.Max(2, size * .035f), color);
        }
    }
}
