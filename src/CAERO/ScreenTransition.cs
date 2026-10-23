using Raylib_cs;

namespace Caero;

internal sealed class ScreenTransition
{
    internal const float FadeDuration = 1;
    internal const float TitleHoldDuration = 2;
    internal MenuAction Destination { get; private set; }
    private float elapsed;
    private bool revealing, blackPresented;
    internal bool Active => Destination != MenuAction.None;
    internal float Opacity
    {
        get
        {
            if (!Active) return 0;
            float t = Math.Clamp(elapsed / FadeDuration, 0, 1);
            float fade = t * t * (3 - 2 * t);
            return revealing ? 1 - fade : fade;
        }
    }
    internal bool Loading => Active && !revealing && Destination != MenuAction.Title && Opacity == 1;
    internal bool ReadyToSwitch => Active && !revealing && blackPresented
        && elapsed >= FadeDuration + (Destination == MenuAction.Title ? TitleHoldDuration : 0);

    internal void Begin(MenuAction destination)
    {
        if (Active) return;
        Destination = destination; elapsed = 0; revealing = blackPresented = false;
    }
    internal void Update(float dt)
    {
        if (!Active) return;
        elapsed += Math.Max(0, dt);
        if (revealing && elapsed >= FadeDuration) Destination = MenuAction.None;
    }
    internal void Reveal()
    {
        revealing = true; elapsed = 0;
    }
    internal void Draw(ConversationUi text)
    {
        if (!Active) return;
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(),
            new Color(0, 0, 0, (int)MathF.Round(Opacity * 255)));
        if (Loading) text.Label("Loading...", 20, 18, 24, Color.White);
    }
    internal void FramePresented()
    {
        if (Active && !revealing && Opacity == 1) blackPresented = true;
    }
}
