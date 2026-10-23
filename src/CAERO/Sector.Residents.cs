using System.Numerics;

namespace Caero;

internal enum ResidentAreaKind { Ground, OpenGround, OpenUpper, Bridge }
internal readonly record struct ResidentArea(float MinX, float MaxX, float MinZ, float MaxZ, float Height, ResidentAreaKind Kind)
{
    internal bool Contains(Vector3 p) => Math.Abs(p.Y - Height) < .04f
        && p.X >= MinX && p.X <= MaxX && p.Z >= MinZ && p.Z <= MaxZ;
    internal Vector3 Sample(Xor128 random) => new(MinX + random.NextSingle() * (MaxX - MinX), Height,
        MinZ + random.NextSingle() * (MaxZ - MinZ));
}

internal sealed partial class Sector
{
    // Semantic floor regions, not fixed spawn points. Collision still decides whether
    // each sampled body and its complete patrol actually fit on the finished map.
    internal List<ResidentArea> ResidentAreas { get; } = [];
    internal Vector3 ResidentLocal(Vector3 p)
    {
        p -= Offset;
        return Rotation switch { 1 => new(p.Z, p.Y, -p.X), 2 => new(-p.X, p.Y, -p.Z),
            3 => new(-p.Z, p.Y, p.X), _ => p };
    }
    internal bool ResidentFloorAllowed(Vector3 world)
    {
        Vector3 p = ResidentLocal(world);
        // Doorways do not make the enclosed tower an eligible interior.
        if (p.X >= 12 && p.X <= 32 && p.Z >= 10 && p.Z <= 34) return false;
        if (ChaosTier > 0 && SkylineStyle is 1 or 3
            && p.X >= -32.2f && p.X <= -11.8f && p.Z >= 10 && p.Z <= 34) return false;
        return ResidentAreas.Any(a => a.Contains(p));
    }
    private void BuildResidentAreas()
    {
        ResidentAreas.Add(new(-38, 38, -38, 38, 0, ResidentAreaKind.Ground));
        ResidentAreas.Add(new(-30, 30, -36, -8, 0, ResidentAreaKind.OpenGround));
        ResidentAreas.Add(new(-30, 30, -36, -8, HallHeight, ResidentAreaKind.OpenUpper));
        // Each island owns half a bridge. A ten-unit seam buffer on each half
        // preserves spacing across independently generated neighboring islands,
        // even with two 2.5-unit patrols heading toward one another.
        ResidentAreas.Add(new(38, 86, -3.6f, 3.6f, 0, ResidentAreaKind.Bridge));
        ResidentAreas.Add(new(-86, -38, -3.6f, 3.6f, 0, ResidentAreaKind.Bridge));
        ResidentAreas.Add(new(-3.6f, 3.6f, 38, 86, 0, ResidentAreaKind.Bridge));
        ResidentAreas.Add(new(-3.6f, 3.6f, -86, -38, 0, ResidentAreaKind.Bridge));
        if (ChaosTier > 0 && SkylineStyle is 0 or 2)
        {
            ResidentAreas.Add(new(-31, -13, 11, 33, 0, ResidentAreaKind.OpenGround));
            ResidentAreas.Add(new(-30, -14, 11, 14, 12, ResidentAreaKind.OpenUpper));
        }
        foreach (var tower in FrameTowers)
            foreach (float y in new[] { 12.7f, 30.7f })
                if (y <= tower.Height + .7f)
                    ResidentAreas.Add(new(tower.X - tower.Width / 2, tower.X + tower.Width / 2,
                        tower.Z - tower.Depth / 2, tower.Z + tower.Depth / 2, y,
                        y < 20 ? ResidentAreaKind.OpenGround : ResidentAreaKind.OpenUpper));
        foreach (var port in SkyPorts)
        {
            Vector3 end = ResidentLocal(port.End);
            end.X = Math.Clamp(end.X, -86, 86); end.Z = Math.Clamp(end.Z, -86, 86);
            if (Math.Abs(end.X) > 80)
                ResidentAreas.Add(new(Math.Min(0, end.X), Math.Max(0, end.X), -1.3f, 1.3f, port.Height, ResidentAreaKind.Bridge));
            else
                ResidentAreas.Add(new(-1.3f, 1.3f, Math.Min(0, end.Z), Math.Max(0, end.Z), port.Height, ResidentAreaKind.Bridge));
        }
    }
}
