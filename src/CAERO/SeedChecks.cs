using System.Globalization;

namespace Caero;

internal static class SeedChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Seed/xor128 test failed: " + message); }

    private static Seed128 Parse(string hex) => new(
        uint.Parse(hex.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        uint.Parse(hex.AsSpan(8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        uint.Parse(hex.AsSpan(16, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        uint.Parse(hex.AsSpan(24, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    internal static void Run()
    {
        // Independent Python hashlib/unsigned-integer reference vectors.
        var vectors = new (int X, int Z, string Expected)[] {
            (0, 0, "E7EF131BAEA0F3665620C22E5CF77C26"), (1, 0, "252246AC8E6277AAC0EC469C8001D71E"),
            (-1, 1, "6A2721E555D9FA221CC0BD0214585BDD"), (12, 34, "D7EDD277CA3DB370ED834D38B0F0F52E"),
            (1, 23, "042775D661CC05DEB94F277750C078FA"), (12, 3, "25BACFDB323AE50BCE045B519A999527"),
            (int.MinValue, int.MaxValue, "4FD55BF88EABFFEE257C62802CD71B1B") };
        var mixed = new (int Seed, string Expected)[] {
            (0, "44EDF7023F3CD841E82919209CE8E41B"), (1701, "733CF42BBD0554D087EF6B4B44552D3A"),
            (-31, "4902A9C3B8A1907873DA051DB757F323"), (int.MinValue, "DB8FC3F77D3AEAB8552FFCF05F039C5B") };
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "ja-JP", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (var (x, z, expected) in vectors)
                    Check(Sector.CoordinateSeedAt(x, z) == Parse(expected), $"SHA-512 first 128 bits at {x}:{z}");
                foreach (var (seed, expected) in mixed)
                    Check(Sector.SeedAt(seed, 0, 0) == Parse(expected), "128-bit world/domain mixing");
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
        Parallel.For(0, 100, i => {
            Check(Sector.CoordinateSeedAt(-1, 1) == Parse(vectors[2].Expected), "concurrent hashing");
            var rng = new Xor128(Sector.SeedAt(1701, 0, 0));
            foreach (uint expected in new uint[] { 3495695415, 1198029112, 3139565485, 1458577678, 1715305035, 1654156813 })
                Check(rng.NextUInt32() == expected, "coordinate seed to xor128 integration");
        });

        Seed128 state = new(123456789, 362436069, 521288629, 88675123);
        var reference = new Xor128(state);
        foreach (uint expected in new uint[] { 3701687786, 458299110, 2500872618, 3633119408, 516391518, 2377269574,
            2599949379, 717229868, 137866584, 395339113, 1301295572, 1728310821 })
            Check(reference.NextUInt32() == expected, "Marsaglia xor128 known sequence");
        Check(new Xor128(state).NextSingle() == .861866295337677f, "fixed 24-bit float conversion");
        Check(new Xor128(state).NextDouble() == .8618663482867633, "fixed 53-bit double conversion");
        var zero = new Xor128(default);
        foreach (uint expected in new uint[] { 1, 1, 1, 2056 }) Check(zero.NextUInt32() == expected, "nonzero fallback for zero seed");
        Check(new Xor128(default).Next(int.MaxValue) == 2056, "rejection sampling discards low biased values");
        Check(new Xor128(default).Next(int.MinValue, int.MaxValue) == int.MinValue + 1, "full signed integer range");
        Check(reference.Next(0) == 0 && reference.Next(-2, -2) == -2, "empty ranges");
        bool invalid = false;
        try { reference.Next(-1); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "negative bound rejected");
        invalid = false;
        try { reference.Next(2, 1); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "reversed range rejected");
        var positive = new Xor128(Sector.SeedAt(123, 0, 0)); var negative = new Xor128(Sector.SeedAt(-123, 0, 0));
        Check(positive.NextUInt32() != negative.NextUInt32(), "signed world seeds stay distinct");
        var sequences = new HashSet<string>();
        foreach (var seed in new[] { new Seed128(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1) })
        {
            var rng = new Xor128(seed);
            sequences.Add(string.Join(",", Enumerable.Range(0, 16).Select(_ => rng.NextUInt32())));
        }
        Check(sequences.Count == 4, "all four state words influence the sequence");
        for (int i = 0; i < 10000; i++)
        {
            int bounded = reference.Next(-10, 20), full = reference.Next(int.MinValue, int.MaxValue);
            float single = reference.NextSingle(); double number = reference.NextDouble();
            Check(bounded is >= -10 and < 20 && full < int.MaxValue && reference.Next() >= 0, "integer output bounds");
            Check(single is >= 0 and < 1 && number is >= 0 and < 1, "floating output bounds");
        }
        Console.WriteLine("SEEDS: 128-bit SHA-512 coordinates, world/domain mixing, xor128 known vectors, full-state use, conversions, rejection, bounds, cultures and concurrency passed.");
    }
}
