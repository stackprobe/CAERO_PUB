using System.Numerics;

namespace Caero;

// Never multiply an absolute island address into a floating-point world position.
internal readonly record struct WorldPosition(long IslandX, long IslandZ, Vector3 Local)
{
    internal WorldPosition Normalized()
    {
        var cell = World.Cell(Local);
        return new(checked(IslandX + cell.X), checked(IslandZ + cell.Z),
            Local - new Vector3((float)cell.X * World.CellSize, 0, (float)cell.Z * World.CellSize));
    }

    internal Vector3 RelativeTo(long originX, long originZ) => Local + Offset(IslandX, IslandZ, originX, originZ);
    internal static Vector3 Offset(long x, long z, long originX, long originZ) =>
        new((float)((Int128)x - originX) * World.CellSize, 0, (float)((Int128)z - originZ) * World.CellSize);
}
