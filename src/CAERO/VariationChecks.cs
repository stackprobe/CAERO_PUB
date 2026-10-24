using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class VariationChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Variation test failed: " + message); }
    public static void Run()
    {
        WingSurfaces();
        var silhouettes = new HashSet<string>();
        for (int style = 0; style < WingCatalog.StyleCount; style++)
        {
            var mesh = WingCatalog.Build(style, 18, 12, 12, Color.Gray, Color.White, Color.LightGray);
            Check(mesh.HasFiniteNormals && mesh.Vertices.All(p => float.IsFinite(p.X + p.Y + p.Z)), "finite module geometry");
            Check(mesh.Vertices.All(p => p.X >= -.01f && p.X <= 18.01f && Math.Abs(p.Z) <= 6.01f), "module fits reserved footprint");
            silhouettes.Add(string.Join(";", mesh.Vertices));
        }
        Check(silhouettes.Count == 20 && WingCatalog.Names.Distinct().Count() == 20, "twenty distinct building silhouettes");
        Check(Sector.StrengthAt(4.999) == 0 && Sector.StrengthAt(5) > 0, "changes start at five");
        Check(Sector.StrengthAt(5) < Sector.StrengthAt(15) && Sector.StrengthAt(15) < Sector.StrengthAt(35)
            && Sector.StrengthAt(35) == Sector.StrengthAt(150), "gradual bounded intensity");
        var materials = new HashSet<int>(); var wings = new HashSet<int>(); var abundance = new HashSet<int>();
        float nearReach = 0, farReach = 0; int nearPatches = 0, farPatches = 0, nearCount = 0, farCount = 0;
        for (int seed = 1; seed <= 64; seed++)
        {
            using var near = Sector.Generate(seed, 5, 0);
            using var far = Sector.Generate(seed, 35, 0);
            foreach (var s in new[] { near, far })
            {
                materials.Add((int)s.WallFinish.X); wings.UnionWith(s.WingStyles); abundance.Add(s.WeedRoots.Count);
                Check(s.MinimumWeedCount >= 10 && s.WeedRoots.Count >= s.MinimumWeedCount, "every island retains at least its previous seeded weed amount");
                Check(s.WeedRoots.Count == Math.Max(s.OriginalWeedCount, s.MinimumWeedCount), "only deficient islands receive extra grass; dense islands retain their count");
                Check(s.WingStyles.Distinct().Count() == s.WingCount, "combinations contain distinct module kinds");
                Check(s.Stone.Vertices.All(p => Math.Abs(p.X - s.Offset.X) <= 96.01f && Math.Abs(p.Z - s.Offset.Z) <= 96.01f), "all variants stay in sector ownership");
            }
            nearReach += near.WingReach; farReach += far.WingReach;
            nearCount += near.WingCount; farCount += far.WingCount;
            nearPatches += near.WeedPatchBudget; farPatches += far.WeedPatchBudget;
        }
        Check(materials.Count == 20 && wings.Count == 20, "all twenty wall and building styles generated");
        Check(abundance.Count > 40 && abundance.Min() >= 10 && abundance.Max() > 150, "minimum vegetation and dense seeded growth");
        Check(farReach > nearReach * 2 && farCount > nearCount * 3 && farPatches > nearPatches * 2,
            "distant populations have larger/combined wings and denser vegetation budgets");
        Console.WriteLine($"VARIATION: 20 wall finishes, 20 building modules, combinations, {abundance.Count} weed quantities and progressive distance intensity passed.");
    }

    internal static void WingSurfaces()
    {
        foreach (var (width, depth, height) in new[] { (18f, 12f, 12f), (6.1f, 6.3f, 6.2f), (33.7f, 19.8f, 18.4f) })
            for (int style = 0; style < WingCatalog.StyleCount; style++)
            {
                var mesh = WingCatalog.Build(style, width, depth, height, Color.Gray, Color.White, Color.LightGray);
                Check(mesh.CountCoplanarOverlaps() == 0, $"no overlapping wing surfaces: {WingCatalog.Names[style]}, {width}/{depth}/{height}");
                Check(mesh.HasFiniteNormals, "clipped wing normals remain finite");
                if (style == 4)
                {
                    var courtyard = new WalkableScene(); mesh.AddCollisionTo(courtyard);
                    for (float x = -1; x <= 1; x += .5f)
                        for (float z = -1; z <= 1; z += .5f)
                        {
                            Check(float.IsNegativeInfinity(courtyard.FloorAt(width / 2 + x, z, .01f)),
                                "courtyard exit has no floor across the player's footprint");
                            Check(!courtyard.Blocked(new(width / 2 + x, -.3f, z)),
                                "courtyard exit is open through the slab thickness");
                        }
                    Check(Math.Abs(courtyard.FloorAt(width / 2, 2, .01f)) < .002f,
                        "courtyard retains a walkable floor around the exit");
                }
                if (style is not (3 or 5)) continue;
                var scene = new WalkableScene(); mesh.AddCollisionTo(scene);
                foreach (float side in new[] { -1f, 1f })
                    for (int step = 1; step < 20; step++)
                    {
                        float reach = style == 3 ? width * .7f : width;
                        float top = style == 3 ? height * .7f : height;
                        float z = side * depth * (style == 3 ? .33f : .32f);
                        float half = depth * (style == 3 ? .125f : .15f);
                        foreach (float dz in new[] { -half * .99f, 0, half * .99f })
                            Check(Math.Abs(scene.FloorAt(reach * step / 20, z + dz, top + .02f) - top) < .002f,
                                "finger/fork roof remains flat and supported across its entire width");
                    }
            }
        Console.WriteLine("WING SURFACES: all 20 styles at three sizes, no coplanar overlaps and supported finger/fork roofs passed.");
        CourtyardEscapeChecks.Run();
    }
}
