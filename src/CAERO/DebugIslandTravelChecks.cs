#if DEBUG
using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class DebugIslandTravelChecks
{
    internal static void Run()
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Debug island travel check failed: " + message); }
        using var world = new World(upload: false);
        var player = new Explorer();
        var conversation = new Conversation();
        var islands = SpecialContent.Data.Objects.Select(d => BigInteger.Parse(d.Island))
            .Distinct().Order().Where(number => number > 1).ToArray();
        for (long z = -8; z <= 8; z++)
            for (long x = -8; x <= 8; x++)
                Check(DebugIslandTravel.Address(Sector.SpiralNumber(x, z)) == (x, z), "spiral address round trip");
        Check(DebugIslandTravel.Address(Sector.SpiralNumber(5000000000, -4000000000)) == (5000000000, -4000000000),
            "large island numbers retain integer precision");
        var item = SpecialContent.Data.Objects.First(d => d.Kind == SpecialKind.Item);
        world.Progress.Collect(item);
        foreach (var key in new[] { KeyboardKey.One, KeyboardKey.Zero, KeyboardKey.Two, KeyboardKey.Kp3,
            KeyboardKey.Kp4, KeyboardKey.Kp5, KeyboardKey.Kp6, KeyboardKey.Kp7, KeyboardKey.Kp8, KeyboardKey.Kp9, KeyboardKey.E })
            Check(!DebugIslandTravel.Handle(world, player, conversation, candidate => candidate == key, radius: 0)
                && world.OriginX == 0 && world.OriginZ == 0, "top-row digits and unrelated keys do not teleport");
        for (int i = 0; i < islands.Length; i++)
        {
            var speaker = world.People.First();
            conversation.Start(speaker);
            player.Flying = true;
            Check(DebugIslandTravel.Handle(world, player, conversation, key => key == KeyboardKey.Kp1, radius: 0), "key handled");
            Check(Sector.SpiralNumber(world.OriginX, world.OriginZ) == islands[i], "correct destination island");
            var sector = world.Sectors.Single(s => s.X == world.OriginX && s.Z == world.OriginZ);
            Check(Vector3.Distance(player.Position, sector.WarpArrivalPosition + Vector3.UnitY * Explorer.EyeHeight) < .001f
                && !world.Blocked(player.Position), "safe arrival on existing warp landing floor");
            Check(!conversation.Active && !speaker.Talking && !player.Flying, "conversation and motion reset");
            Check(world.Progress.Collected(item.Id) && !world.Progress.Escaped, "inventory preserved and ending not activated");
        }
        var lastPosition = player.Position;
        Check(!DebugIslandTravel.Handle(world, player, conversation, key => key == KeyboardKey.Kp1, radius: 0)
            && player.Position == lastPosition && Sector.SpiralNumber(world.OriginX, world.OriginZ) == islands[^1],
            "no higher special island leaves player in place");
        Check(DebugIslandTravel.Handle(world, player, conversation, key => key == KeyboardKey.Kp0, radius: 0)
            && world.OriginX == 0 && world.OriginZ == 0 && !world.Blocked(player.Position), "keypad zero returns to island 1");
        // Player may cross a cell boundary before the world's origin is recentered.
        player.Position = new Vector3(World.CellSize * 2, Explorer.EyeHeight, 0);
        BigInteger current = Sector.SpiralNumber(2, 0);
        BigInteger next = islands.First(number => number > current);
        Check(DebugIslandTravel.Handle(world, player, conversation, key => key == KeyboardKey.Kp1, radius: 0)
            && Sector.SpiralNumber(world.OriginX, world.OriginZ) == next,
            "next destination uses player's current ordinary island, even before recentering");
        var beforeItems = SaveStore.Capture(player, world);
        conversation.Start(world.People.First());
        Check(DebugIslandTravel.Handle(world, player, conversation, key => key == KeyboardKey.Kp2, radius: 0),
            "keypad two grants items and requests immediate autosave from the session");
        var allItems = SpecialContent.Data.Objects.Where(d => d.Kind == SpecialKind.Item).Select(d => d.Id).Order().ToArray();
        Check(world.Progress.Snapshot().Collected.Order().SequenceEqual(allItems)
            && world.Progress.CanFly && !world.Progress.Escaped && !conversation.Active,
            "all item IDs are acquired, abilities enabled, dialogue closed and ending not triggered");
        var afterItems = SaveStore.Capture(player, world);
        Check(afterItems.X == beforeItems.X && afterItems.Y == beforeItems.Y && afterItems.Z == beforeItems.Z
            && afterItems.IslandX == beforeItems.IslandX && afterItems.IslandZ == beforeItems.IslandZ
            && afterItems.Yaw == beforeItems.Yaw && afterItems.Pitch == beforeItems.Pitch,
            "granting items preserves the current island, position and facing");
        player.Flying = true;
        Check(DebugIslandTravel.Handle(world, player, conversation, key => key == KeyboardKey.Kp2, radius: 0),
            "removing all items also requests immediate autosave");
        Check(world.Progress.Snapshot().Collected.Length == 0 && !world.Progress.CanFly && !player.Flying
            && world.Progress.DashBonus == 0 && world.Progress.JumpMultiplier == 1 && !world.Progress.Escaped,
            "all-item state toggles to empty and cancels flight and all ability bonuses");
        Check(player.Position == new Vector3(beforeItems.X, beforeItems.Y, beforeItems.Z)
            && player.Yaw == beforeItems.Yaw && player.Pitch == beforeItems.Pitch,
            "removing items preserves position and facing");
        DebugIslandTravel.Handle(world, player, conversation, key => key == KeyboardKey.Kp2, radius: 0);
        Check(world.Progress.Snapshot().Collected.Order().SequenceEqual(allItems), "empty state toggles back to all items");
        Console.WriteLine("DEBUG ISLAND TRAVEL: island sequence, keypad zero return, last-island no-op, safe arrivals and keypad two all/empty item toggle passed.");
    }
}
#endif
