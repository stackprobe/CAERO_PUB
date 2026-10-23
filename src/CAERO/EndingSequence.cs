namespace Caero;

internal sealed class EndingSequence
{
    internal const string Message = "あなたは、脱出した。";
    internal const float WhiteDuration = 2, TextStart = 2.4f, TextDuration = 1.4f;
    internal const float ImageStart = 5, ImageDuration = 3, ButtonDelay = 3;
    internal float Elapsed { get; private set; }
    private bool returnArmed;
    private static float Fade(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    internal float WhiteOpacity => Fade(Elapsed / WhiteDuration);
    internal float TextOpacity => Fade((Elapsed - TextStart) / TextDuration) * (1 - Fade(Elapsed - ImageStart));
    internal float ImageOpacity => Fade((Elapsed - ImageStart) / ImageDuration);
    internal bool CanReturn => Elapsed >= ImageStart + ImageDuration + ButtonDelay;
    internal void Update(float dt) => Elapsed += Math.Max(0, dt);
    internal bool ReturnInput(bool pressed, bool held)
    {
        if (!CanReturn) { returnArmed = false; return false; }
        // Require a release after the button appears. Earlier clicks/held keys cannot carry over.
        if (!held) returnArmed = true;
        return returnArmed && pressed;
    }
}
