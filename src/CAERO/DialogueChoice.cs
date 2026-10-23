namespace Caero;

// A yes/no question follows the last common page. Each answer supplies its
// remaining pages and optionally another choice at their end.
// An empty branch ends the conversation immediately.
internal sealed record DialogueChoice
{
    public required string[] YesPages { get; init; }
    public required string[] NoPages { get; init; }
    public DialogueChoice? YesChoice { get; init; }
    public DialogueChoice? NoChoice { get; init; }
    internal IEnumerable<string> AllPages => YesPages.Concat(NoPages)
        .Concat(YesChoice?.AllPages ?? []).Concat(NoChoice?.AllPages ?? []);
    internal bool IsValid => ValidTree(0)
        && AllPages.Sum(p => (long)p.Length) <= 20000;
    private bool ValidTree(int depth) => depth < 8
        && ValidPages(YesPages, allowEmpty: true) && ValidPages(NoPages, allowEmpty: true)
        && (YesChoice == null || (YesPages.Length > 0 && YesChoice.ValidTree(depth + 1)))
        && (NoChoice == null || (NoPages.Length > 0 && NoChoice.ValidTree(depth + 1)));
    internal static bool ValidPages(string[]? pages, bool allowEmpty = false)
        => pages != null && (allowEmpty || pages.Length > 0)
            && pages.All(p => !string.IsNullOrWhiteSpace(p) && !p.Any(c => char.IsControl(c) && c != '\n'))
            && pages.Sum(p => (long)p.Length) <= 20000;
}
