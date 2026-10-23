using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed record SkyPort(int Dx, int Dz, float Height, Vector3 LocalEnd)
{
    internal Vector3 Offset { get; set; }
    public Vector3 End => LocalEnd + Offset;
}
internal sealed record FrameTower(float X, float Z, float Width, float Depth, float Height, int First, int Count);

internal sealed partial class Sector
{
    public int ChaosTier => TierAt(X, Z);
    public float HighRoof { get; private set; }
    public int SkylineStyle { get; private set; }
    public int FloatingStructureCount { get; private set; }
    public List<SkyPort> SkyPorts { get; } = [];
    internal List<FrameTower> FrameTowers { get; } = [];
    private int highRiseSurfaceFirst, highRiseSurfaceCount;
    internal int CountHighRiseOverlaps() => Stone.CountCoplanarOverlaps(highRiseSurfaceFirst, highRiseSurfaceCount);
    public static int TierAt(long x, long z)
    {
        double distance = Math.Sqrt((double)x * x + (double)z * z);
        return distance == 0 ? 0 : distance < 3 ? 1 : distance < 6 ? 2 : distance < 10 ? 3 : distance < 15 ? 4 : 5;
    }
    // Edge contracts use world coordinates; a sector's rotation and RNG cannot change them.
    public static float SkyHeightAt(long x, long z, int dx, int dz)
    {
        int tier = Math.Min(TierAt(x, z), TierAt(x + dx, z + dz));
        return tier < 2 ? 0 : (tier + 1) * 24;
    }
    private void BuildChaos()
    {
        if (ChaosTier == 0) return; // Preserve the entire starting island and its random sequence.
        var chaos = new Xor128(SeedAt(worldSeed ^ 941, X, Z));
        SkylineStyle = chaos.Next(4);
        HighRoof = (2 + ChaosTier * 4 + chaos.Next(3)) * 12;
        highRiseSurfaceFirst = Stone.VertexCount;
        BuildHighRise();
        BuildRoofCrossing();
        BuildSkyNetwork();
        highRiseSurfaceCount = Stone.VertexCount - highRiseSurfaceFirst;
        if (ChaosTier >= 2) BuildSatellites(chaos);
        if (ChaosTier >= 3) BuildFloatingCourt(chaos);
        if (ChaosTier >= 4) BuildSuspendedFragments(chaos);
    }
    private void BuildHighRise()
    {
        float roof = HighRoof;
        var entranceLevels = new HashSet<float> { TowerFloors * 12 };
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var d = Rotate(new(dx, 0, dz));
            entranceLevels.Add(SkyHeightAt(X, Z, (int)d.X, (int)d.Z));
        }
        foreach (float x in new[] { -31.3f, -12.7f })
            foreach (float z in new[] { 10.7f, 33.3f })
                Box(new(x, roof / 2, z), new(1.4f, roof, 1.4f), pale);
        var route = new List<Vector3> { new(-22, 0, 36), new(-22, 0, 18), new(-26, 0, 13) };
        for (int level = 0; level < (int)(roof / 12); level++)
        {
            float y = level * 12;
            Stair(new(-26, y, 14), new(-26, y + 6, 30), 4);
            Deck(-30, -14, 30, 33, y + 6);
            Stair(new(-18, y + 6, 30), new(-18, y + 12, 14), 4);
            if (y + 12 < roof) Deck(-30, -14, 11, 14, y + 12);
            foreach (float z in new[] { 10.5f, 33.5f })
                // End beams at the inner pillar faces, rather than layering their faces.
                Box(new(-22, y + 11.5f, z), new(17.2f, 1, 1));
            foreach (float x in new[] { -31.5f, -12.5f })
            {
                float lo = x == -12.5f && entranceLevels.Contains(y + 12) ? 14 : 11.4f;
                // Corridor floors occupy the entrance gap; no beam top under the floor.
                Box(new(x, y + 11.5f, (lo + 32.6f) / 2), new(1, 1, 32.6f - lo));
                if (SkylineStyle == 1 || SkylineStyle == 3 && level % 2 == 0)
                    Box(new(x, y + 7, 22), new(1, 5, 18));
            }
            if (SkylineStyle == 2 && level % 3 == 2)
            {
                // Cantilever shelves and alternating fins keep the stair core open.
                Deck(-37, -32, 9, 35, y + 12);
                Box(new(-34.5f, y + 5.65f, 10), new(5, 11.3f, .7f));
            }
            route.AddRange([new(-26, y + 6, 31.5f), new(-18, y + 6, 31.5f),
                new(-18, y + 12, 12), new(-26, y + 12, 12)]);
        }
        // The last return flight has an opening instead of a slab through the stairs.
        // Pillar tops own the four corners. Infill only the remaining roof area.
        Deck(-31, -20, 11.4f, 32.6f, roof); Deck(-16, -13, 11.4f, 32.6f, roof);
        Deck(-20, -16, 11.4f, 14, roof); Deck(-20, -16, 30, 32.6f, roof);
        Deck(-30.6f, -13.4f, 11, 11.4f, roof); Deck(-30.6f, -13.4f, 32.6f, 33, roof);
        Route("highrise switchbacks", route.ToArray());
        Route("highrise approach", new(0, 0, 36), new(-22, 0, 36));
    }
    private void BuildRoofCrossing()
    {
        float y = TowerFloors * 12;
        // Extend the return landing, keeping the entire ascending stair opening clear.
        Deck(-14, 12, 11.5f, 14, y); // Clear the pillar and touch the old tower wall top.
        Box(new(-1, y - 1.1f, 12.5f), new(26, .8f, 1), pale);
        Route("old roof to highrise", new(18, y, 12.5f), new(-22, y, 12.5f));
    }
    private void BuildSkyNetwork()
    {
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            Vector3 direction = Rotate(new(dx, 0, dz));
            float y = SkyHeightAt(X, Z, (int)direction.X, (int)direction.Z);
            if (y == 0) continue;
            SkyPorts.Add(new((int)direction.X, (int)direction.Z, y, Transform(new(dx * 96, y, dz * 96))));
        }
        foreach (var group in SkyPorts.GroupBy(p => p.Height))
        {
            float y = group.Key;
            Deck(-14, -4, 11.5f, 14, y); Deck(-4, 4, -4, 14, y);
            foreach (var port in group)
            {
                // Convert the world direction back into the local construction frame.
                Vector3 d = port.End - Transform(new(0, y, 0));
                Vector3 local = Rotation switch { 1 => new(d.Z, 0, -d.X), 2 => -d, 3 => new(-d.Z, 0, d.X), _ => d };
                int dx = Math.Sign(local.X), dz = Math.Sign(local.Z);
                if (dx == 1) Deck(4, 96, -1.5f, 1.5f, y);
                if (dx == -1) Deck(-96, -4, -1.5f, 1.5f, y);
                if (dz == 1) Deck(-1.5f, 1.5f, 14, 96, y);
                if (dz == -1) Deck(-1.5f, 1.5f, -96, -4, y);
                if (dx != 0)
                    foreach (float side in new[] { -1.4f, 1.4f })
                        Box(new(dx * 51, y + .5f, side), new(90, 1, .2f), pale);
                else
                    foreach (float side in new[] { -1.4f, 1.4f })
                    {
                        float lo = dz > 0 ? 16 : -96, hi = dz > 0 ? 96 : -6;
                        Box(new(side, y + .5f, (lo + hi) / 2), new(.2f, 1, hi - lo), pale);
                    }
                Route("sky port", new(-22, y, 12), new(0, y, 12), new(0, y, 0), new(dx * 95.5f, y, dz * 95.5f));
            }
        }
    }
    private void BuildSatellites(Xor128 chaos)
    {
        var sites = new (float X, float Z)[] { (-65, -58), (64, 61), (-65, 63) };
        for (int i = 0; i < Math.Min(3, ChaosTier - 1); i++)
        {
            var site = sites[i]; float height = HighRoof * (.65f + chaos.NextSingle() * .55f);
            float width = chaos.Next(14, 24), depth = chaos.Next(16, 27);
            if ((SkylineStyle + i) % 3 == 0)
            {
                int first = Stone.VertexCount;
                foreach (float dx in new[] { -width / 2, width / 2 })
                    foreach (float dz in new[] { -depth / 2, depth / 2 })
                        Box(new(site.X + dx, (height + World.SeaFloor) / 2, site.Z + dz), new(2, height - World.SeaFloor, 2));
                for (float y = 12; y <= height; y += 18)
                {
                    // Corner pillars own their full cross-sections. Partition each
                    // floor around them so slab edges never overlay pillar faces.
                    Box(new(site.X, y, site.Z), new(width - 2, 1.4f, depth + 2), pale);
                    foreach (float dx in new[] { -width / 2, width / 2 })
                        Box(new(site.X + dx, y, site.Z), new(2, 1.4f, depth - 2), pale);
                }
                FrameTowers.Add(new(site.X, site.Z, width, depth, height, first, Stone.VertexCount - first));
            }
            else
            {
                // Setbacks, off-center crowns and exposed ribs give different silhouettes.
                for (int band = 0; band < 3; band++)
                {
                    float bottom = band == 0 ? World.SeaFloor : height * band / 3;
                    float top = height * (band + 1) / 3;
                    float inset = band * 2;
                    Box(new(site.X + (band % 2) * 2, (bottom + top) / 2, site.Z),
                        new(width - inset, top - bottom, depth - inset));
                    Box(new(site.X + (band % 2) * 2, top + .5f, site.Z), new(width - inset + 3, 1, depth - inset + 3), pale);
                }
                foreach (float side in new[] { -1f, 1f })
                    Box(new(site.X + side * (width / 2 + 1), height / 2, site.Z), new(1, height, depth * .35f), pale);
            }
        }
    }
    private void BuildFloatingCourt(Xor128 chaos)
    {
        int first = Stone.VertexCount;
        float y = HighRoof;
        // An intentionally suspended court connected to the accessible tower roof.
        // Split the elbow into disjoint slabs to avoid coplanar floor overlaps.
        Deck(-23.5f, -20.5f, -48.5f, 11, y);
        Deck(-23.5f, -20.5f, -51.5f, -48.5f, y);
        Deck(-20.5f, 48, -51.5f, -48.5f, y);
        Deck(48, 72, -62, -38, y, 2);
        int floors = 1 + chaos.Next(ChaosTier - 1);
        FloatingCourtFloors = floors;
        for (int level = 0; level < floors; level++)
        {
            float bottom = y + level * 12;
            foreach (float x in new[] { 49f, 71f })
                foreach (float z in new[] { -61f, -39f }) Box(new(x, bottom + 6, z), new(1.5f, 12, 1.5f), pale);
            Deck(48, 72, -62, -38, bottom + 12, 1.2f);
            Box(new(60, bottom + 8, -61), new(19, 5, 1));
            if (level % 2 == 0) Box(new(71, bottom + 8, -50), new(1, 5, 20));
        }
        FloatingStructureCount++;
        Route("floating court", new(-22, y, 12), new(-22, y, -50), new(50, y, -50));
        // The passage joins the local west face. Rotate the entire staircase
        // onto another face without consuming construction or warp randomness.
        FloatingCourtStairTurns = 1 + new Xor128(SeedAt(worldSeed ^ 0x53544149, X, Z)).Next(3);
        CourtStairDeck(34.5f, 48, -61, -58, y);
        for (int level = 0; level <= floors; level++)
        {
            float bottom = y + level * 12;
            // Perimeter circulation avoids the warp machines and links each
            // landing to the existing tower/warp-room approach, including roof.
            Vector3[] access = FloatingCourtStairTurns switch
            {
                1 => [new(50, bottom, -59.5f), new(69.5f, bottom, -59.5f), new(69.5f, bottom, -58)],
                2 => [new(50, bottom, -59.5f), new(69.5f, bottom, -59.5f), new(69.5f, bottom, -40.5f), new(68, bottom, -40.5f)],
                _ => [new(50, bottom, -59.5f), new(50.5f, bottom, -59.5f), new(50.5f, bottom, -42)]
            };
            Route("floating court landing access", access);
            if (level == 0)
                Route("floating court tower access", new(50, y, -50), access[0]);
            if (level == floors) break;
            Stair(CourtStairPoint(new(42, bottom, -58)), CourtStairPoint(new(42, bottom + 6, -42)), 3);
            CourtStairDeck(34.5f, 43.5f, -42, -39, bottom + 6);
            Stair(CourtStairPoint(new(36, bottom + 6, -42)), CourtStairPoint(new(36, bottom + 12, -58)), 3);
            CourtStairDeck(34.5f, 48, -61, -58, bottom + 12);
            CourtStairBox(new(39, bottom + 6.5f, -39.1f), new(9, 1, .2f));
            CourtStairBox(new(41.25f, bottom + 12.5f, -60.9f), new(13.5f, 1, .2f));
            Route("floating court exterior stairs", new Vector3[] {
                new(52, bottom, -59.5f), new(42, bottom, -59.5f), new(42, bottom, -58),
                new(42, bottom + 6, -40.5f), new(36, bottom + 6, -40.5f),
                new(36, bottom + 12, -59.5f), new(52, bottom + 12, -59.5f)
            }.Select(CourtStairPoint).ToArray());
        }
        Stone.RemoveCoplanarOverlaps(first);
    }
    internal int FloatingCourtStairTurns { get; private set; }
    // Quarter turns about the court center, before the island's own rotation.
    internal Vector3 CourtStairPoint(Vector3 point)
    {
        Vector3 p = point - new Vector3(60, 0, -50);
        return new Vector3(60, 0, -50) + (FloatingCourtStairTurns switch
        {
            1 => new Vector3(-p.Z, p.Y, p.X),
            2 => new Vector3(-p.X, p.Y, -p.Z),
            3 => new Vector3(p.Z, p.Y, -p.X),
            _ => p
        });
    }
    private void CourtStairDeck(float x0, float x1, float z0, float z1, float y)
    {
        Vector3 a = CourtStairPoint(new(x0, y, z0)), b = CourtStairPoint(new(x1, y, z1));
        Deck(Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Z, b.Z), Math.Max(a.Z, b.Z), y);
    }
    private void CourtStairBox(Vector3 center, Vector3 size)
        => Box(CourtStairPoint(center), FloatingCourtStairTurns % 2 == 0 ? size : new(size.Z, size.Y, size.X), pale);

    private void BuildSuspendedFragments(Xor128 chaos)
    {
        for (int i = 0; i < ChaosTier * 2; i++)
        {
            float x = i % 2 == 0 ? chaos.Next(48, 80) : chaos.Next(-82, -48);
            float z = chaos.Next(40, 82), bottom = HighRoof + chaos.Next(24, 100);
            float height = chaos.Next(18, 68), width = chaos.Next(3, 8);
            Stone.Box(new(x, bottom + height / 2, z), new(width, height, width), pale, chaos.NextSingle() * .8f);
            if (i % 3 == 0)
            {
                Box(new(x, bottom, z), new(16, 1.5f, 12));
                Box(new(x + 5, bottom + height * .6f, z), new(24, 2, 3), pale);
            }
            FloatingStructureCount++;
        }
    }
}
