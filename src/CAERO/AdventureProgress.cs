using System.Numerics;

namespace Caero;

internal sealed record AdventureSave(string[] Collected, bool Escaped = false, string? ExitId = null);

internal sealed class AdventureProgress
{
    private readonly HashSet<string> collected = new(StringComparer.Ordinal);
    private string? exitId;
    internal bool Escaped { get; private set; }
    internal string EndingText => EndingSequence.Message;
    private int Count(ItemEffect effect) => collected.Count(id => SpecialContent.ById.TryGetValue(id, out var d) && d.Effect == effect);
    internal int AbilityCount(ItemEffect effect) => Count(effect);
    internal bool CanFly => Count(ItemEffect.Flight) > 0;
    internal float DashBonus => Count(ItemEffect.Dash) * SpecialContent.Data.DashBonusPerItem;
    internal float JumpMultiplier => 1 + Count(ItemEffect.Jump) * SpecialContent.Data.JumpBonusPerItem;
    internal bool Collected(string id) => collected.Contains(id);
    internal bool Collect(SpecialDefinition item) => item.Kind == SpecialKind.Item && collected.Add(item.Id);
    internal void Escape(string id) { Escaped = true; exitId = id; }
    internal AdventureSave Snapshot() => new(collected.Order(StringComparer.Ordinal).ToArray(), Escaped, exitId);
    internal void Restore(AdventureSave? save)
    {
        collected.Clear(); Escaped = save?.Escaped ?? false; exitId = save?.ExitId;
        foreach (string id in save?.Collected ?? [])
            if (id != null && SpecialContent.ById.TryGetValue(id, out var d) && d.Kind == SpecialKind.Item) collected.Add(id);
    }
}

internal static class SpecialInteraction
{
    internal static string PotionQuestion(ItemEffect effect) => (effect switch
    {
        ItemEffect.Dash => "青い薬がある。",
        ItemEffect.Jump => "赤い薬がある。",
        ItemEffect.Flight => "緑の薬がある。",
        _ => throw new ArgumentOutOfRangeException(nameof(effect))
    }) + "\n飲んでみますか？";
    internal static SpecialObject? Target(World world, Vector3 eye, Vector3 forward)
        => world.Sectors.SelectMany(s => s.SpecialObjects)
            .Where(o => o.Definition.Kind != SpecialKind.Item || !world.Progress.Collected(o.Definition.Id))
            .Where(o => Vector3.DistanceSquared(eye, o.ExaminePoint) < 3.4f * 3.4f
                && Vector3.Dot(Vector3.Normalize(o.ExaminePoint - eye + new Vector3(.00001f)), forward) > .65f)
            .OrderBy(o => Vector3.DistanceSquared(eye, o.ExaminePoint))
            .FirstOrDefault(o => world.Sectors.All(s => !s.Collision.Obstructs(eye, o.ExaminePoint)));

    internal static void Examine(World world, Explorer player, SpecialObject target, Conversation conversation, Action? onCollected = null)
    {
        // Revalidate on activation; stale targets cannot collect twice or read
        // through walls after a teleport, rebase or unload.
        if (player.Flying || world.Progress.Escaped || !ReferenceEquals(Target(world, player.Position, player.Forward), target)) return;
        var d = target.Definition;
        if (d.Kind == SpecialKind.Exit)
        {
            conversation.Confirm(d.Name, string.Join('\n', d.Pages), () =>
            {
                // Save and resume on the supported approach, looking at the relic.
                Vector3 eye = target.Foot + target.LocalFacing * 2.19f + Vector3.UnitY * Explorer.EyeHeight;
                player.GoTo(new("EXIT RELIC", eye, target.ExaminePoint));
                world.Progress.Escape(d.Id);
            });
            return;
        }
        if (d.Kind == SpecialKind.Item)
        {
            conversation.Confirm(d.Name, PotionQuestion(d.Effect), () =>
            {
                if (player.Flying || world.Progress.Escaped || !ReferenceEquals(Target(world, player.Position, player.Forward), target)
                    || !world.Progress.Collect(d)) return;
                player.DrinkPotion();
                conversation.Read(target);
                onCollected?.Invoke();
            });
            return;
        }
        conversation.Read(target);
    }
}
