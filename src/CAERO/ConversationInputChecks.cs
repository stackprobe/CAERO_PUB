using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class ConversationInputChecks
{
    internal static void Run()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Choice input check failed: " + message); }
        var conversation = new Conversation();
        void Press(KeyboardKey key) => ConversationInput.Handle(conversation, candidate => candidate == key);
        int confirmed = 0;
        foreach (var key in new[] { KeyboardKey.E, KeyboardKey.Space, KeyboardKey.Enter })
        {
            conversation.Confirm("Test", "Question?", () => confirmed++);
            for (int i = 0; i < 25; i++)
            {
                Press(KeyboardKey.Right); Press(key);
                Check(conversation.Active && conversation.SelectedYes && confirmed == 0,
                    "rapid selection and confirmation cannot act on a newly visible choice");
            }
            conversation.Update(Conversation.ChoiceInputDelay - .001f);
            Press(KeyboardKey.Right); Press(key);
            Check(conversation.Active && conversation.SelectedYes && confirmed == 0, "input is still blocked before the deadline");
            conversation.Update(.002f);
            Press(KeyboardKey.Right); Press(key);
            Check(!conversation.Active && confirmed == 0, "No works normally after the delay");
        }
        conversation.Confirm("Test", "Question?", () => confirmed++);
        conversation.Update(Conversation.ChoiceInputDelay); Press(KeyboardKey.Enter); Press(KeyboardKey.Enter);
        Check(confirmed == 1 && !conversation.Active, "Yes executes exactly once after the delay");

        var definition = SpecialContent.Data.Objects[0] with
        {
            Pages = ["First question"],
            Choice = new() { YesPages = ["First Yes"], NoPages = ["Second question"],
                NoChoice = new() { YesPages = ["Second Yes"], NoPages = ["Second No"] } }
        };
        using var document = new SpecialObject { Definition = definition, LocalFoot = Vector3.Zero,
            LocalFacing = Vector3.UnitZ, Geometry = new() };
        conversation.Read(document); conversation.Update(100);
        Check(conversation.AwaitingChoice && !conversation.ChoiceInputReady,
            "natural typewriter completion starts the delay when the choice first appears");
        Press(KeyboardKey.Enter);
        Check(conversation.Text == "First question", "the reveal frame cannot also confirm the default answer");
        conversation.Update(Conversation.ChoiceInputDelay); Press(KeyboardKey.Right); Press(KeyboardKey.Enter);
        conversation.Update(100);
        Check(conversation.Text == "Second question" && !conversation.ChoiceInputReady && conversation.SelectedYes,
            "nested questions receive their own fresh delay");
        Press(KeyboardKey.Right); Press(KeyboardKey.Space);
        Check(conversation.Text == "Second question" && conversation.SelectedYes, "nested question also resists repeated input");
        conversation.Update(Conversation.ChoiceInputDelay); Press(KeyboardKey.Right); Press(KeyboardKey.Space);
        Check(conversation.Text == "Second No", "nested No is accepted after its delay");
        conversation.Update(100); Press(KeyboardKey.Space);
        Check(!conversation.Active, "ordinary answer pages can close without a choice delay");

        conversation.Read(document); Press(KeyboardKey.Enter);
        Check(conversation.AwaitingChoice && !conversation.ChoiceInputReady, "instant text reveal starts a fresh delay too");
        Press(KeyboardKey.Escape);
        Check(!conversation.Active, "Escape may still cancel without confirming any answer");
        conversation.Read(document); Press(KeyboardKey.Enter); Press(KeyboardKey.Enter);
        Check(conversation.Text == "First question" && !conversation.ChoiceInputReady,
            "reopening a conversation never inherits an expired delay");
        conversation.Close();
        Console.WriteLine("CHOICE INPUT: 0.2-second lock, rapid presses, all confirm keys, natural/instant reveal, nested questions, cancel and reopen passed.");
    }
}
