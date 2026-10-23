using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed partial class Sector
{
    // Separate random streams keep the existing terrain, stairs and bridge contracts stable.
    internal double IslandDistance => Math.Sqrt((double)X * X + (double)Z * Z);
    internal Vector3 WallFinish { get; private set; }
    internal float IdentityStrength => StrengthAt(IslandDistance);
    internal static float StrengthAt(double distance) => distance < 5 ? 0 : .15f + .85f * Math.Clamp((float)(distance - 5) / 30, 0, 1);
    internal List<int> WingStyles { get; } = [];
    internal float WingReach { get; private set; }
    internal int WeedPatchBudget { get; private set; }
    internal float WeedDensity { get; private set; }
    internal int MinimumWeedCount { get; private set; }
    internal int OriginalWeedCount { get; private set; }
    internal List<Vector3> WeedRoots { get; } = [];
    internal int WingCount { get; private set; }
    internal float WingBaseHeight { get; private set; }
    internal int SculptureStyle { get; private set; } = -1;
    internal int SculptureCount { get; private set; }
    internal Vector3 SculptureFoot { get; private set; }
    internal int SculptureColor { get; private set; } = -1;
    internal float SculpturePlinthHalfSize => X == 0 && Z == 0 ? 2.4f : 1.9f;
    internal float SculptureYaw { get; private set; }
    internal Vector3 SculptureMin { get; private set; }
    internal Vector3 SculptureMax { get; private set; }
    internal int SculptureFirst { get; private set; }
    internal int SculpturePlacementAttempts { get; private set; }

    private void VaryLampRequests()
    {
        if (X == 0 && Z == 0) return;
        var lamps = new Xor128(SeedAt(worldSeed ^ 0x4c414d50, X, Z));
        float Between(float lo, float hi) => lo + lamps.NextSingle() * (hi - lo);
        lampRequests.Clear();
        Lamp(new(Between(-18, -9), 0, Between(5, 8)));
        Lamp(new(Between(9, 18), 0, Between(5, 8)));
        foreach (float sign in new[] { -1f, 1f })
        {
            float d = sign * Between(48, 85), side = lamps.Next(2) == 0 ? -2.8f : 2.8f;
            Lamp(lamps.Next(2) == 0 ? new(d, 0, side) : new(side, 0, d));
        }
        Lamp(new(Between(-26, -14), 0, Between(-26, -17)));
        Vector3 roofLamp = new(-24, HallHeight, -28);
        for (int attempt = 0; attempt < 64; attempt++)
        {
            var candidate = new Vector3(Between(-26, -8), HallHeight, Between(-30, -17));
            if (!NearWalkingRoute(candidate)) { roofLamp = candidate; break; }
        }
        Lamp(roofLamp);
        Lamp(new(14, 0, Between(19, 27)));
        Lamp(new(14, TowerFloors * 12, Between(18, 27)));
        Lamp(new(Between(-29, -28), 0, Between(35, 36)));
    }

    private bool NearWalkingRoute(Vector3 local)
    {
        Vector3 p = Transform(local);
        foreach (var route in Routes)
            for (int i = 1; i < route.Points.Length; i++)
            {
                Vector3 a = route.Points[i - 1], d = route.Points[i] - a;
                float t = Math.Clamp(Vector3.Dot(p - a, d) / Math.Max(d.LengthSquared(), .0001f), 0, 1);
                if (Vector3.DistanceSquared(p, a + d * t) < 1.5f * 1.5f) return true;
            }
        return false;
    }

    private void BuildIslandIdentity()
    {
        if (IslandDistance >= 5)
        {
            var identity = new Xor128(SeedAt(worldSeed ^ 0x4944454e, X, Z));
            WallFinish = new(identity.Next(1, 21), .65f + identity.NextSingle() * (1 + IdentityStrength), identity.NextSingle());
            BuildLateralWings(new Xor128(SeedAt(worldSeed ^ 0x57494e47, X, Z)));
        }
        BuildWeeds(new Xor128(SeedAt(worldSeed ^ 0x57454544, X, Z)));
    }

    private void BuildLateralWings(Xor128 identity)
    {
        // Disjoint height bands in a reserved west-side volume; all bridges stay clear.
        float strength = IdentityStrength;
        int count = 1 + (int)(strength * 3) + identity.Next(strength > .5f ? 2 : 1);
        var available = Enumerable.Range(0, WingCatalog.StyleCount).ToList();
        WingBaseHeight = 24 + identity.Next(3) * 12;
        float spacing = MathF.Floor((HighRoof - WingBaseHeight - 24) / count / 12) * 12;
        for (int i = 0; i < count; i++)
        {
            int index = identity.Next(available.Count), style = available[index]; available.RemoveAt(index);
            float y = WingBaseHeight + spacing * i;
            float z = 20 + identity.NextSingle() * 4;
            float reach = 4 + strength * (12 + identity.NextSingle() * 18);
            float depth = 5 + strength * (7 + identity.NextSingle() * 8);
            float height = Math.Min(spacing - 3, 5 + strength * (7 + identity.NextSingle() * 8));
            var module = WingCatalog.Build(style, reach, depth, height, stone, pale, floor);
            module.Transform(p => new(-p.X, p.Y, -p.Z), new(-37.4f, y, z));
            Stone.Append(module);
            if (style is 0 or 7 or 9 or 15)
            {
                ResidentAreas.Add(new(-37.4f - reach, -37.4f, z - depth / 2, z + depth / 2, y, ResidentAreaKind.OpenGround));
                if (style == 9)
                    ResidentAreas.Add(new(-37.4f - reach, -37.4f, z - depth / 2, z + depth / 2, y + height / 2, ResidentAreaKind.OpenUpper));
            }
            float join = SkylineStyle == 2 && y % 36 == 0 ? -37 : -32;
            Deck(-37.4f, join, z - 1.5f, z + 1.5f, y);
            WingStyles.Add(style); WingReach = Math.Max(WingReach, reach);
            WingCount++;
        }
    }

    private void BuildSculpture()
    {
        var choices = new Xor128(SeedAt(worldSeed ^ 0x5343554c, X, Z));
        var placement = new Xor128(SeedAt(worldSeed ^ 0x504c4143, X, Z));
        int style = choices.Next(SculptureCatalog.StyleCount), color = choices.Next(SculptureCatalog.ColorCount);
        float yaw = choices.NextSingle() * MathF.Tau;
        bool firstIsland = X == 0 && Z == 0;
        if (firstIsland) { style = SculptureCatalog.StartStyle; color = -1; }
        var mesh = firstIsland ? SculptureCatalog.BuildStartMonument() : SculptureCatalog.Build(style, color, pale);
        mesh.Transform(p => Vector3.Transform(p, Matrix4x4.CreateRotationY(yaw)), Vector3.Zero);
        Vector3 min = mesh.Vertices.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min);
        Vector3 max = mesh.Vertices.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max);
        var support = new WalkableScene();
        Stone.AddCollisionTo(support); Land.AddCollisionTo(support);
        var origin = new Vector3(-27 + placement.NextSingle() * 54, 0, -28 + placement.NextSingle() * 58);
        // First shift a colliding choice nearby; then try other seeded sites and a bounded grid.
        IEnumerable<Vector3> Candidates()
        {
            yield return origin;
            float phase = placement.NextSingle() * MathF.Tau;
            for (int ring = 1; ring <= 6; ring++)
                for (int spoke = 0; spoke < 8; spoke++)
                {
                    float a = phase + spoke * MathF.Tau / 8;
                    yield return origin + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * ring * .8f;
                }
            for (int i = 0; i < 64; i++)
                yield return new(-27 + placement.NextSingle() * 54, 0, -28 + placement.NextSingle() * 58);
            for (int x = -26; x <= 26; x += 4)
                for (int z = -26; z <= 30; z += 4) yield return new(x, 0, z);
        }
        foreach (var p in Candidates())
        {
            SculpturePlacementAttempts++;
            Vector3 lo = p + min - new Vector3(.35f, 0, .35f), hi = p + max + new Vector3(.35f);
            if (lo.X < compassLocal.X + 2.1f && hi.X > compassLocal.X - 2.1f
                && lo.Z < compassLocal.Z + 2.1f && hi.Z > compassLocal.Z - 2.1f) continue;
            if (lo.X < numberPlateClearMax.X && hi.X > numberPlateClearMin.X
                && lo.Z < numberPlateClearMax.Z && hi.Z > numberPlateClearMin.Z) continue;
            lo.Y = p.Y + .04f; // The plinth may touch its supporting floor.
            if (lo.X < -37 || hi.X > 37 || lo.Z < -37 || hi.Z > 37) continue;
            float plinthRadius = SculpturePlinthHalfSize * (Math.Abs(MathF.Cos(yaw)) + Math.Abs(MathF.Sin(yaw)));
            if (!PlacementSupport.HasFlatSupport(support, p, new(plinthRadius))) continue;
            if (support.SurfaceOverlapsBox(lo, hi) || RouteCrossesSculpture(lo, hi)) continue;
            mesh.Transform(v => v, p);
            SculptureFirst = Stone.VertexCount;
            Stone.Append(mesh);
            SculptureCount++;
            SculptureStyle = style; SculptureColor = color; SculptureYaw = yaw;
            SculptureFoot = Transform(p);
            var corners = new[] { Transform(lo), Transform(new(lo.X, lo.Y, hi.Z)),
                Transform(new(hi.X, hi.Y, lo.Z)), Transform(hi) };
            SculptureMin = corners.Aggregate(Vector3.Min); SculptureMax = corners.Aggregate(Vector3.Max);
            return;
        }
        // A crowded future layout must omit a prop rather than put it through a wall.
    }

    private bool RouteCrossesSculpture(Vector3 min, Vector3 max)
    {
        Vector3 Local(Vector3 p)
        {
            p -= Offset;
            return Rotation switch { 1 => new(p.Z, p.Y, -p.X), 2 => new(-p.X, p.Y, -p.Z),
                3 => new(-p.Z, p.Y, p.X), _ => p };
        }
        min -= new Vector3(.85f, 2, .85f); max += new Vector3(.85f, 0, .85f);
        // Also keep the three existing residents' anchors/patrols clear.
        var residentPaths = IslandDistance < 5
            ? new[] { new Vector3(2, 0, 3), new(-8, 0, 24), new(-14, 0, -22) }
                .Select(a => new[] { Transform(a), Transform(a + new Vector3(0, 0, 2.5f)) })
            : Enumerable.Empty<Vector3[]>();
        foreach (var points in Routes.Select(r => r.Points).Concat(residentPaths))
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 a = Local(points[i - 1]), d = Local(points[i]) - a;
                float enter = 0, leave = 1;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (Math.Abs(d[axis]) < .00001f)
                    { if (a[axis] < min[axis] || a[axis] > max[axis]) { enter = 2; break; } }
                    else
                    {
                        float t0 = (min[axis] - a[axis]) / d[axis], t1 = (max[axis] - a[axis]) / d[axis];
                        enter = Math.Max(enter, Math.Min(t0, t1)); leave = Math.Min(leave, Math.Max(t0, t1));
                    }
                }
                if (enter <= leave) return true;
            }
        return false;
    }

    private void BuildWeeds(Xor128 weeds)
    {
        var support = new WalkableScene();
        Stone.AddCollisionTo(support);
        // Preserve the current scatter; only top up islands below the previous generator's amount.
        var abundance = new Xor128(SeedAt(worldSeed ^ 0x4142554e, X, Z));
        WeedDensity = MathF.Pow(abundance.NextSingle(), 1.6f);
        int patches = (int)((3 + WeedDensity * 85) * (.25f + IdentityStrength * .75f));
        if (WeedDensity < .035f) patches = 0;
        WeedPatchBudget = patches;
        foreach (var tuft in ScatterWeeds(weeds, support, patches, false))
        { WeedRoots.Add(Transform(tuft.Root)); Land.Append(tuft.Mesh); }
        OriginalWeedCount = WeedRoots.Count;
        var baselineRandom = new Xor128(SeedAt(worldSeed ^ 0x57454544, X, Z));
        var baseline = ScatterWeeds(baselineRandom, support, baselineRandom.Next(12, 36), true).ToList();
        MinimumWeedCount = baseline.Count;
        foreach (var tuft in baseline)
        {
            if (WeedRoots.Count >= MinimumWeedCount) break;
            Vector3 root = Transform(tuft.Root);
            // Far-coordinate rounding can put distinct blade clusters at one root.
            // Keep their different leaves instead of dropping a tuft below the minimum.
            WeedRoots.Add(root); Land.Append(tuft.Mesh);
        }
    }

    private IEnumerable<(Vector3 Root, Geometry Mesh)> ScatterWeeds(Xor128 weeds, WalkableScene support, int patches, bool baseline)
    {
        for (int patch = 0; patch < patches; patch++)
        {
            // Shared 2.4-unit paving joints, with island-specific clusters and bare areas.
            float x = weeds.Next(-13, 14) * 2.4f, z = weeds.Next(-13, 14) * 2.4f;
            float y = weeds.Next(3) == 0 && z < -10 ? HallHeight : 0;
            int tufts = baseline ? weeds.Next(3, 10) : 1 + weeds.Next(2 + (int)(WeedDensity * 12 * IdentityStrength));
            for (int tuft = 0; tuft < tufts; tuft++)
            {
                var root = new Vector3(x, y, z + (weeds.NextSingle() - .5f) * 2.1f);
                if (!PlacementSupport.HasFlatSupport(support, root, new(.18f, .18f))) continue;
                var mesh = new Geometry();
                float height = baseline ? .25f + weeds.NextSingle() * .9f
                    : .12f + weeds.NextSingle() * (.25f + IdentityStrength * 1.1f);
                var color = new Color(65 + weeds.Next(28), 96 + weeds.Next(40), 44 + weeds.Next(24), 180);
                for (int blade = 0; blade < 5; blade++)
                {
                    float angle = weeds.NextSingle() * MathF.Tau;
                    var side = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * .055f;
                    var tip = root + new Vector3(MathF.Sin(angle) * .22f, height * (.5f + weeds.NextSingle() * .5f), MathF.Cos(angle) * .22f);
                    Vector3 a = root - side, b = root + side;
                    // Double-sided blades; foliage tags intentionally add no collision.
                    mesh.Triangle(a, b, tip, color); mesh.Triangle(b, a, tip, color);
                }
                yield return (root, mesh);
            }
        }
    }
}
