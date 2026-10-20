using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class VariationChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Variation test failed: " + message); }
    public static void Run()
    {
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
}
