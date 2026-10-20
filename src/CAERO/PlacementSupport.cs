using System.Numerics;

namespace Caero;

// Shared placement gate for props that require a flat supporting surface.
internal static class PlacementSupport
{
    public static bool HasFlatSupport(WalkableScene structure, Vector3 foot, Vector2 halfSize)
    {
        const float tolerance = .02f;
        // Check the center, edge midpoints and corners, not just the origin.
        foreach (float dx in new[] { -halfSize.X, 0, halfSize.X })
            foreach (float dz in new[] { -halfSize.Y, 0, halfSize.Y })
            {
                float height = structure.FloorAt(foot.X + dx, foot.Z + dz, foot.Y + tolerance);
                if (!float.IsFinite(height) || Math.Abs(height - foot.Y) > tolerance) return false;
            }
        return true;
    }
}
