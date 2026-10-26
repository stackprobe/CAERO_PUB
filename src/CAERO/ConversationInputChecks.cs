using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class ConversationInputChecks
{
    internal static void Run()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Conversation input check failed: " + message); }
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
        Check(conversation.Active, "ordinary answer pages also wait after natural text completion");
        conversation.Update(Conversation.InputDelay); Press(KeyboardKey.Space);
        Check(!conversation.Active, "ordinary answer pages can close after the delay");

        conversation.Read(document); Press(KeyboardKey.Enter);
        Check(conversation.AwaitingChoice && !conversation.ChoiceInputReady, "instant text reveal starts a fresh delay too");
        Press(KeyboardKey.Escape);
        Check(conversation.Active, "Escape also waits after text reveal");
        conversation.Update(Conversation.InputDelay); Press(KeyboardKey.Escape);
        Check(!conversation.Active, "Escape cancels after the delay without confirming any answer");
        conversation.Read(document); Press(KeyboardKey.Enter); Press(KeyboardKey.Enter);
        Check(conversation.Text == "First question" && !conversation.ChoiceInputReady,
            "reopening a conversation never inherits an expired delay");
        conversation.Close();

        foreach (var key in new[] { KeyboardKey.E, KeyboardKey.Space, KeyboardKey.Enter, KeyboardKey.Escape })
        foreach (bool instant in new[] { false, true })
        {
            using var pages = new SpecialObject
            {
                Definition = definition with { Pages = ["First page", "Last page"], Choice = null },
                LocalFoot = Vector3.Zero, LocalFacing = Vector3.UnitZ, Geometry = new()
            };
            conversation.Read(pages);
            Press(KeyboardKey.Escape);
            Check(conversation.Active, "opening starts a fresh close delay");
            for (int page = 0; page < 2; page++)
            {
                if (instant) Press(KeyboardKey.Enter);
                else conversation.Update(100);
                for (int i = 0; i < 25; i++) Press(key);
                Check(conversation.Active && conversation.Page == page, "rapid presses cannot skip or close a newly revealed page");
                conversation.Update(Conversation.InputDelay - .001f); Press(key);
                Check(conversation.Active && conversation.Page == page, "page input stays blocked before the deadline");
                conversation.Update(.002f); Press(key);
                if (key == KeyboardKey.Escape) break;
                Check(page == 0 ? conversation.Active && conversation.Page == 1 : !conversation.Active,
                    "page advance and final close work after the delay");
            }
            Check(!conversation.Active, "close is accepted after the delay");
        }
        Console.WriteLine("CONVERSATION INPUT: 0.2-second lock, rapid presses, all confirm keys, page advance, final close, natural/instant reveal, nested questions, Escape and reopen passed.");
    }
}
