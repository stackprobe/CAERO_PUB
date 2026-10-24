using System.Numerics;
using Raylib_cs;

namespace Caero;

// Times are measured from startup, rather than from the end of each preceding fade.
internal sealed class StartupSequence
{
    internal const double LogoIn = .5, LogoVisible = 1, LogoOut = 3, TitleIn = 3.5, Finished = 4;
    private double elapsed;
    internal bool Active => elapsed < Finished;
    internal float LogoOpacity => Fade((elapsed - LogoIn) / (LogoVisible - LogoIn))
        * (1 - Fade((elapsed - LogoOut) / (TitleIn - LogoOut)));
    internal float TitleOpacity => Fade((elapsed - TitleIn) / (Finished - TitleIn));
    internal void Update(double dt) => elapsed += Math.Max(0, dt);
    private static float Fade(double value)
    {
        float t = (float)Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

internal sealed class StartupCredit : IDisposable
{
    private readonly Texture2D logo;
    internal StartupCredit()
    {
        using var stream = typeof(StartupCredit).Assembly.GetManifestResourceStream("CAERO.RaylibLogo.png")
            ?? throw new InvalidDataException("raylib logo is missing.");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var image = Raylib.LoadImageFromMemory(".png", bytes.ToArray());
        try { logo = Raylib.LoadTextureFromImage(image); }
        finally { Raylib.UnloadImage(image); }
        if (!Raylib.IsTextureValid(logo)) throw new InvalidDataException("Cannot load raylib logo.");
        Raylib.SetTextureFilter(logo, TextureFilter.Bilinear);
    }
    internal void Draw(StartupSequence sequence, GameMenus menus, GameFlow flow, bool canContinue)
    {
        int width = Raylib.GetScreenWidth(), height = Raylib.GetScreenHeight();
        Raylib.ClearBackground(Color.Black);
        if (sequence.TitleOpacity > 0)
        {
            menus.Draw(flow, canContinue, showItems: !sequence.Active);
            Raylib.DrawRectangle(0, 0, width, height, new Color(0, 0, 0,
                (int)MathF.Round((1 - sequence.TitleOpacity) * 255)));
        }
        else if (sequence.LogoOpacity > 0)
        {
            // Use the unmodified official square logo, centered and without stretching.
            float size = Math.Min(256, Math.Min(width, height) * .4f);
            var tint = new Color(255, 255, 255, (int)MathF.Round(sequence.LogoOpacity * 255));
            const string caption = "Powered by";
            int fontSize = (int)Math.Clamp(size * .08f, 14, 20);
            Raylib.DrawText(caption, (width - Raylib.MeasureText(caption, fontSize)) / 2,
                (int)((height - size) / 2 - Math.Max(12, size * .07f) - fontSize), fontSize, tint);
            Raylib.DrawTexturePro(logo, new(0, 0, logo.Width, logo.Height),
                new((width - size) / 2, (height - size) / 2, size, size), Vector2.Zero, 0,
                tint);
        }
    }
    public void Dispose() => Raylib.UnloadTexture(logo);
}
