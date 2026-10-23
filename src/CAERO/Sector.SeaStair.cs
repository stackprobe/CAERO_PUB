using System.Numerics;

namespace Caero;

internal sealed partial class Sector
{
    internal Geometry SeaStairGeometry { get; } = new();
    internal bool HasSeaStair { get; private set; } = true;
    private readonly WalkableScene seaStairCollision = new();
    private SceneChunk? seaStairChunk;

    internal bool SeaStairOverlaps(DiagonalBridge bridge)
    {
        if (!HasSeaStair || Int128.Abs((Int128)X - bridge.From.X) > 1
            || Int128.Abs((Int128)Z - bridge.From.Z) > 1) return false;
        Vector3 offset = WorldPosition.Offset(X, Z, bridge.From.X, bridge.From.Z) + bridge.Collision.Offset;
        // Check the real tread heights and the player's clearance against the
        // bridge's actual deck/rails, not an unbuilt or merely nearby bridge.
        int steps = (int)MathF.Ceiling(20 / .301f);
        for (int i = 0; i < steps; i++)
            foreach (float lane in new[] { -2.5f, 0, 2.5f })
            {
                Vector3 foot = new(-25 + lane, -20 + 20f * (i + 1) / steps, 86 - 48f * (i + .5f) / steps);
                if (bridge.Collision.Blocked(Rotate(foot) + offset + Vector3.UnitY * Explorer.EyeHeight)) return true;
            }
        return false;
    }

    internal void OmitSeaStair()
    {
        if (!HasSeaStair) return;
        HasSeaStair = false;
        Collision.Additional = null;
        SeaStairGeometry.Clear();
        BridgeJunctionSurface.Clear();
        Routes.RemoveAll(r => r.Name == "sea arrival");
        if (seaStairChunk != null)
        {
            Chunks.Remove(seaStairChunk);
            seaStairChunk.Dispose(); seaStairChunk = null;
        }
    }
}
