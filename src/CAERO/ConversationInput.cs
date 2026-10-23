using Raylib_cs;

namespace Caero;

// Shared by normal play and the capture/debug loop.
internal static class ConversationInput
{
    internal static void Poll(Conversation conversation) => Handle(conversation, key => Raylib.IsKeyPressed(key));

    internal static void Handle(Conversation conversation, Func<KeyboardKey, bool> pressed)
    {
        if (pressed(KeyboardKey.Escape)) { conversation.Close(); return; }
        if (conversation.AwaitingChoice && !conversation.ChoiceInputReady) return;
        if (pressed(KeyboardKey.Left) || pressed(KeyboardKey.Up) || pressed(KeyboardKey.A) || pressed(KeyboardKey.W))
            conversation.SelectAnswer(true);
        if (pressed(KeyboardKey.Right) || pressed(KeyboardKey.Down) || pressed(KeyboardKey.D) || pressed(KeyboardKey.S))
            conversation.SelectAnswer(false);
        if (pressed(KeyboardKey.E) || pressed(KeyboardKey.Space) || pressed(KeyboardKey.Enter)) conversation.Advance();
    }
}
