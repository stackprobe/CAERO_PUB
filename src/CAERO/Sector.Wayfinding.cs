using System.Globalization;
using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed partial class Sector
{
    internal BigInteger IslandNumber => SpiralNumber(X, Z);
    internal Vector3 CompassCenter { get; private set; }
    internal Vector3 NumberPlateCenter { get; private set; }
    internal Vector3 NumberPlateNormal => Rotate(numberPlateNormal);
    internal bool? NumberIsPrime => numberPrime?.Result;
    internal bool WayfindingDisposed { get; private set; }
    private IncrementalPrime? numberPrime;
    private Vector3 numberPlateNormal;
    private Vector3 numberPlateClearMin, numberPlateClearMax;
    private Vector3 compassLocal;

    // North is -Z. 1 at the origin, then east, north, west, south.
    internal static BigInteger SpiralNumber(long x, long z)
    {
        BigInteger a = x, b = z, r = BigInteger.Max(BigInteger.Abs(a), BigInteger.Abs(b));
        BigInteger last = (2 * r + 1) * (2 * r + 1);
        if (b == r) return last - (r - a);
        if (a == -r) return last - 2 * r - (r - b);
        if (b == -r) return last - 4 * r - (a + r);
        return last - 6 * r - (b + r);
    }

    private Vector3 Unrotate(Vector3 p) => Rotation switch
    {
        1 => new(p.Z, p.Y, -p.X), 2 => new(-p.X, p.Y, -p.Z),
        3 => new(-p.Z, p.Y, p.X), _ => p
    };

    private void BuildWayfinding()
    {
        var placement = new Xor128(SeedAt(worldSeed ^ 0x434f4d50, X, Z));
        var support = new WalkableScene();
        Stone.AddCollisionTo(support);
        IEnumerable<Vector3> Candidates()
        {
            for (int i = 0; i < 160; i++)
                yield return new(-29 + placement.NextSingle() * 36, 0, 7 + placement.NextSingle() * 26);
            for (int z = 7; z <= 33; z += 3)
                for (int x = -29; x <= 7; x += 3) yield return new(x, 0, z);
        }
        bool found = false;
        foreach (var p in Candidates())
        {
            if (!PlacementSupport.HasFlatSupport(support, p, new(2.1f))) continue;
            if (support.SurfaceOverlapsBox(p + new Vector3(-2.1f, .02f, -2.1f), p + new Vector3(2.1f, 2.2f, 2.1f))) continue;
            if (WeedRoots.Any(w => Vector3.DistanceSquared(w, Transform(p)) < 2.8f * 2.8f)) continue;
            compassLocal = p; found = true; break;
        }
        if (!found) throw new InvalidOperationException($"No compass site on island {X},{Z}.");
        CompassCenter = Transform(compassLocal);

        // Each pixel is part of the actual surface, with no raised or coplanar overlay.
        Color dark = new(38, 52, 51, 253), brass = new(205, 176, 108, 253), light = new(233, 222, 185, 253);
        const int size = 65;
        int[,] dial = new int[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - 32, dy = y - 32, radius = MathF.Sqrt(dx * dx + dy * dy);
                dial[x, y] = radius > 29 && radius < 31 || x == 0 || y == 0 || x == 64 || y == 64 ? 1 : 0;
                if (Math.Abs(dx) + Math.Abs(dy) / 6 < 4 && Math.Abs(dy) < 23
                    || Math.Abs(dy) + Math.Abs(dx) / 6 < 4 && Math.Abs(dx) < 23)
                    dial[x, y] = dy < 0 ? 2 : 1;
            }
        Stamp(dial, "N", 30, 4, 2); Stamp(dial, "S", 30, 54, 1);
        Stamp(dial, "W", 5, 29, 1); Stamp(dial, "E", 55, 29, 1);
        InlayPixels(dial, .0625f, (u, v) => compassLocal + Unrotate(new(u, 0, -v)), [dark, brass, light]);

        BuildNumberSign(support);
    }

    private void BuildNumberSign(WalkableScene support)
    {
        string number = IslandNumber.ToString(CultureInfo.InvariantCulture);
        int columns = Math.Min(10, number.Length), rows = (number.Length + 9) / 10;
        int scale = columns <= 3 ? 3 : columns <= 6 ? 2 : 1;
        int width = Math.Max(48, columns * 4 * scale + 12), height = 16 + rows * 6 * scale + 5;
        var plaque = new int[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int edge = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
                plaque[x, y] = edge < 2 ? 2 : edge == 2 ? 3 : 0;
            }
        Stamp(plaque, "ISLAND", (width - 35) / 2, 5, 2);
        for (int row = 0; row < rows; row++)
        {
            string line = number.Substring(row * 10, Math.Min(10, number.Length - row * 10));
            Stamp(plaque, line, (width - (line.Length * 4 - 1) * scale) / 2, 16 + row * 6 * scale, 1, scale);
        }
        foreach (int x in new[] {4, width - 6})
            foreach (int y in new[] {4, height - 6})
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++) plaque[x + dx, y + dy] = 4;
        float cell = Math.Min(.14f, Math.Min(4.8f / width, 2.8f / height));
        float half = width * cell / 2;
        var sites = new Xor128(SeedAt(worldSeed ^ 0x5349474e, X, Z));
        int firstSide = sites.Next(4);
        Vector3 center = default, normal = default, right = default;
        bool found = false;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            int side = (firstSide + attempt / 16) % 4;
            float t = sites.NextSingle();
            (center, normal) = side switch
            {
                0 => (new Vector3((sites.Next(2) == 0 ? 16.5f : 27.5f) + (t * 2 - 1) * (3.2f - half), 2.4f, 34), Vector3.UnitZ),
                1 => (new Vector3(13.3f + half + t * (17.4f - 2 * half), 2.4f, 10), -Vector3.UnitZ),
                2 => (new Vector3(12, 2.4f, 10.3f + half + t * (23.4f - 2 * half)), -Vector3.UnitX),
                _ => (new Vector3(32, 2.4f, 10.3f + half + t * (23.4f - 2 * half)), Vector3.UnitX)
            };
            // A plaque needs masonry behind its entire width, including when
            // the seeded entrance layout opens this facade.
            if ((TowerEntranceMask & (1 << side)) != 0)
            {
                float axis = side < 2 ? center.X : center.Z;
                float door = side < 2 ? 22 : 12.5f;
                float doorHalf = side < 2 ? 2 : 1.5f;
                if (Math.Abs(axis - door) < half + doorHalf + .1f) continue;
            }
            right = Vector3.Cross(Vector3.UnitY, normal);
            var edgeA = center - right * half + normal * .02f;
            var edgeB = center + right * half + normal * 2.4f;
            Vector3 clearMin = Vector3.Min(edgeA, edgeB), clearMax = Vector3.Max(edgeA, edgeB);
            clearMin.Y = .05f; clearMax.Y = center.Y + height * cell / 2 + .1f;
            if (support.SurfaceOverlapsBox(clearMin, clearMax)) continue;
            Vector3 approach = center + normal * 1.1f; approach.Y = 0;
            if (!PlacementSupport.HasFlatSupport(support, approach, new(.35f))) continue;
            if (support.Blocked(approach + Vector3.UnitY * Explorer.EyeHeight)) continue;
            numberPlateClearMin = clearMin; numberPlateClearMax = clearMax;
            found = true; break;
        }
        if (!found) throw new InvalidOperationException($"No readable sign wall on island {X},{Z}.");
        NumberPlateCenter = Transform(center); numberPlateNormal = normal;
        InlayPixels(plaque, cell, (u, v) => center + right * u + Vector3.UnitY * v,
            [new(222, 216, 192, 251), new(30, 43, 45, 252), new(45, 63, 65, 253), new(182, 153, 93, 253), new(164, 166, 155, 253)]);
        numberPrime = new(IslandNumber);
    }

    internal void StepNumberPrime()
    {
        numberPrime?.Step();
        if (NumberIsPrime.HasValue)
            foreach (var chunk in Chunks) chunk.PrimeNumber = NumberIsPrime == true;
    }

    private void DisposeWayfinding()
    { WayfindingDisposed = true; numberPrime?.Dispose(); }

    private void InlayPixels(int[,] pixels, float cell, Func<float, float, Vector3> point, Color[] palette)
    {
        int width = pixels.GetLength(0), height = pixels.GetLength(1);
        var footprint = new Geometry(); var surface = new Geometry();
        void Rect(Geometry g, int x0, int y0, int x1, int y1, Color color)
        {
            Vector3 P(int x, int y) => point((x - width * .5f) * cell, (height * .5f - y) * cell);
            g.Quad(P(x0, y1), P(x1, y1), P(x1, y0), P(x0, y0), color);
        }
        Rect(footprint, 0, 0, width, height, palette[0]);
        // Merge same-colour runs to keep geometry and collision costs low.
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width;)
            {
                int start = x, shade = pixels[x++, y];
                while (x < width && pixels[x, y] == shade) x++;
                Rect(surface, start, y, x, y + 1, palette[shade]);
            }
        int[] remap = Stone.Inlay(footprint, surface);
        int Map(int index) => remap[index / 3];
        // Surface audits retain the original tower ranges after floor/wall clipping.
        highRiseSurfaceCount = Map(highRiseSurfaceFirst + highRiseSurfaceCount) - Map(highRiseSurfaceFirst);
        highRiseSurfaceFirst = Map(highRiseSurfaceFirst);
        for (int i = 0; i < FrameTowers.Count; i++)
        {
            var tower = FrameTowers[i];
            FrameTowers[i] = tower with { First = Map(tower.First), Count = Map(tower.First + tower.Count) - Map(tower.First) };
        }
    }

    private static void Stamp(int[,] pixels, string text, int x, int y, int shade, int scale = 1)
    {
        foreach (char c in text)
        {
            string glyph = c switch
            {
                '0' => "111101101101111", '1' => "010110010010111", '2' => "111001111100111",
                '3' => "111001111001111", '4' => "101101111001001", '5' => "111100111001111",
                '6' => "111100111101111", '7' => "111001010010010", '8' => "111101111101111",
                '9' => "111101111001111", 'N' => "10001110011100110101100111001110001",
                'E' => "11111100001000011110100001000011111",
                'S' => "01111100001000001110000010000111110",
                'I' => "11111001000010000100001000010011111", 'L' => "10000100001000010000100001000011111",
                'A' => "01110100011000111111100011000110001", 'D' => "11110100011000110001100011000111110",
                'W' => "10001100011000110101101011010101010", _ => throw new ArgumentOutOfRangeException(nameof(text))
            };
            int width = char.IsDigit(c) ? 3 : 5, height = char.IsDigit(c) ? 5 : 7;
            for (int row = 0; row < height; row++)
                for (int col = 0; col < width; col++)
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            pixels[x + col * scale + dx, y + row * scale + dy] = glyph[row * width + col] == '1' ? shade : 0;
            x += (width + 1) * scale;
        }
    }
}
