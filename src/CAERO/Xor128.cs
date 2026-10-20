using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Caero;

// Words follow the SHA-512 digest in order, each read big-endian.
internal readonly record struct Seed128(uint X, uint Y, uint Z, uint W)
{
    internal static Seed128 Hash(string text)
    {
        byte[] digest = SHA512.HashData(Encoding.ASCII.GetBytes(text));
        return new(BinaryPrimitives.ReadUInt32BigEndian(digest), BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(4)),
            BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(8)), BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(12)));
    }

    internal Seed128 WithWorldSeed(int seed)
    {
        // Hash the signed world/domain seed to avoid correlated states for nearby seeds.
        Seed128 domain = Hash("world:" + seed.ToString(CultureInfo.InvariantCulture));
        return new(X ^ domain.X, Y ^ domain.Y, Z ^ domain.Z, W ^ domain.W);
    }
}

// Marsaglia's xor128: four unsigned 32-bit words, shifts (11, 19, 8).
// Keep this recurrence and all output conversions fixed across generator versions.
internal sealed class Xor128
{
    private uint x, y, z, w;

    internal Xor128(Seed128 seed)
    {
        (x, y, z, w) = (seed.X, seed.Y, seed.Z, seed.W);
        // Zero is the only forbidden state. Define its handling deterministically.
        if ((x | y | z | w) == 0) w = 1;
    }

    internal uint NextUInt32()
    {
        uint t = x ^ (x << 11);
        x = y; y = z; z = w;
        return w = w ^ (w >> 19) ^ t ^ (t >> 8);
    }

    private uint Below(uint bound)
    {
        // Rejection sampling avoids the bias of a plain remainder operation.
        uint threshold = unchecked(0u - bound) % bound;
        uint value;
        do { value = NextUInt32(); } while (value < threshold);
        return value % bound;
    }

    internal int Next() => (int)Below(int.MaxValue);
    internal int Next(int maxValue) => Next(0, maxValue);
    internal int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(maxValue));
        if (minValue == maxValue) return minValue;
        return (int)(minValue + (long)Below((uint)((long)maxValue - minValue)));
    }

    // Exactly 24/53 random bits; both intervals are [0, 1).
    internal float NextSingle() => (NextUInt32() >> 8) * (1f / 16777216f);
    internal double NextDouble() => (((ulong)(NextUInt32() >> 5) << 26) | (NextUInt32() >> 6)) * (1.0 / 9007199254740992.0);
}
