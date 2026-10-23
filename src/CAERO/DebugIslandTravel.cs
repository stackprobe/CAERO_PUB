#if DEBUG
using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class DebugIslandTravel
{
    private static readonly BigInteger[] Islands = SpecialContent.Data.Objects
        .Select(d => BigInteger.Parse(d.Island, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();

    internal static (long X, long Z) Address(BigInteger number)
    {
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        // Find the spiral ring with integer arithmetic, including large island numbers.
        BigInteger low = 0, high = 1;
        while (BigInteger.Pow(2 * high + 1, 2) < number) high *= 2;
        while (low < high)
        {
            BigInteger mid = (low + high) / 2;
            if (BigInteger.Pow(2 * mid + 1, 2) < number) low = mid + 1;
            else high = mid;
        }
        BigInteger r = low, d = BigInteger.Pow(2 * r + 1, 2) - number;
        if (r == 0) return (0, 0);
        (BigInteger x, BigInteger z) = d < 2 * r ? (r - d, -r)
            : d < 4 * r ? (-r, -r + d - 2 * r)
            : d < 6 * r ? (-r + d - 4 * r, r) : (r, r - d + 6 * r);
        return ((long)x, (long)z);
    }

    internal static bool Poll(World world, Explorer player, Conversation conversation)
        => Handle(world, player, conversation, key => Raylib.IsKeyPressed(key));

    internal static bool Handle(World world, Explorer player, Conversation conversation,
        Func<KeyboardKey, bool> pressed, int radius = 1)
    {
        BigInteger destination;
        if (pressed(KeyboardKey.Kp0)) destination = 1;
        else if (pressed(KeyboardKey.Kp1))
        {
            var current = world.GlobalCell(player.Position);
            BigInteger number = Sector.SpiralNumber(current.X, current.Z);
            destination = Islands.FirstOrDefault(island => island > number);
            if (destination == 0) return false;
        }
        else if (pressed(KeyboardKey.Kp2))
        {
            conversation.Close();
            var items = SpecialContent.Data.Objects.Where(d => d.Kind == SpecialKind.Item).ToArray();
            if (items.All(item => world.Progress.Collected(item.Id)))
            {
                world.Progress.Restore(world.Progress.Snapshot() with { Collected = [] });
                player.Flying = false;
            }
            else
                foreach (var item in items) world.Progress.Collect(item);
            return true;
        }
        else return false;
        var (x, z) = Address(destination);
        conversation.Close();
        world.Teleport(player, new(x, z, Vector3.Zero), radius: radius, destinationView: sector =>
        {
            Vector3 eye = sector.WarpArrivalPosition + Vector3.UnitY * Explorer.EyeHeight;
            return new("DEBUG ISLAND TRAVEL", eye, eye - Vector3.UnitZ);
        });
        return true;
    }
}
#endif
