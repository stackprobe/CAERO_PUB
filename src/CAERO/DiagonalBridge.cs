using System.Numerics;
using Raylib_cs;

namespace Caero;

// One complete bridge owns its mesh and collision, including the four-cell corner.
internal sealed class DiagonalBridge : IDisposable
{
    internal (long X, long Z) From { get; }
    internal (long X, long Z) To { get; }
    internal WalkableScene Collision { get; } = new();
    internal SceneChunk? Chunk { get; }
    internal IReadOnlyList<Vector3> Path { get; }
    internal int JunctionOverlaps { get; }
    internal int JunctionOverlapsRemoved { get; }
    internal IReadOnlyList<Vector3> EntrancePanelCenters { get; }

    internal DiagonalBridge(Sector from, Sector to, bool upload)
    {
        From = (from.X, from.Z); To = (to.X, to.Z);
        int dz = to.Z > from.Z ? 1 : -1;
        var geometry = new Geometry();
        // Ground-level satellites (including their rotated pillars/fins) stay
        // within +/-80 of an island center. The sea lanes at 86/106 leave room
        // for the entire six-wide deck outside all four islands' buildings.
        Path = new Vector3[] { new(38, 0, 24 * dz), new(86, 0, 24 * dz),
            new(86, 0, 86 * dz), new(106, 0, 106 * dz),
            new(168, 0, 106 * dz), new(168, 0, 154 * dz) };
        var path = Path.ToArray();
        var joins = new Vector3[path.Length];
        Vector3 Left(Vector3 direction) => new(-direction.Z, 0, direction.X);
        for (int i = 0; i < path.Length; i++)
        {
            Vector3 before = Vector3.Normalize(path[Math.Max(1, i)] - path[Math.Max(0, i - 1)]);
            Vector3 after = Vector3.Normalize(path[Math.Min(path.Length - 1, i + 1)] - path[Math.Min(path.Length - 2, i)]);
            Vector3 normal = Left(before) + Left(after);
            joins[i] = normal / Vector3.Dot(normal, Left(after));
        }
        // Adjacent strips share one mitered edge, avoiding overlapping floors
        // and gaps at turns. Rails follow those same joins continuously.
        void Strip(float left, float right, float bottom, float top, Color color)
        {
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 a = path[i - 1] + joins[i - 1] * left + Vector3.UnitY * top;
                Vector3 b = path[i] + joins[i] * left + Vector3.UnitY * top;
                Vector3 c = path[i] + joins[i] * right + Vector3.UnitY * top;
                Vector3 d = path[i - 1] + joins[i - 1] * right + Vector3.UnitY * top;
                Vector3 down = Vector3.UnitY * (bottom - top);
                geometry.Quad(a, b, c, d, color);
                geometry.Quad(d + down, c + down, b + down, a + down, color);
                geometry.Quad(a, a + down, b + down, b, color);
                geometry.Quad(c, c + down, d + down, d, color);
                if (i == 1) geometry.Quad(d, d + down, a + down, a, color);
                if (i == path.Length - 1) geometry.Quad(b, b + down, c + down, c, color);
            }
        }
        Strip(3, -3, -.7f, 0, new Color(168, 163, 143, 240));
        // Leave the first/last two units free for the island entrances.
        path[0] += Vector3.Normalize(path[1] - path[0]) * 2;
        path[^1] -= Vector3.Normalize(path[^1] - path[^2]) * 2;
        Strip(3, 2.6f, 0, 1.3f, new Color(198, 188, 160, 220));
        Strip(-2.6f, -3, 0, 1.3f, new Color(198, 188, 160, 220));
        var junction = new Geometry();
        junction.Append(to.BridgeJunctionSurface);
        junction.Transform(p => p, new(World.CellSize, 0, dz * World.CellSize));
        junction.Append(from.BridgeJunctionSurface);
        JunctionOverlapsRemoved = geometry.CountCoplanarOverlaps(junction);
        // The last sea-stair tread extends beyond the island edge at y=0.
        // Let that existing surface own the overlap, including its end wall.
        geometry.Inlay(junction, new Geometry());
        Vector3 entryDirection = Vector3.Normalize(Path[1] - Path[0]);
        Vector3 exitDirection = Vector3.Normalize(Path[^2] - Path[^1]);
        EntrancePanelCenters = new[] { Path[0] + entryDirection * 4, Path[^1] + exitDirection * 4 };
        AddEntrancePanel(geometry, EntrancePanelCenters[0], entryDirection);
        AddEntrancePanel(geometry, EntrancePanelCenters[1], exitDirection);
        JunctionOverlaps = geometry.CountCoplanarOverlaps(junction);
        geometry.AddCollisionTo(Collision);
        if (upload)
            Chunk = new SceneChunk { Detailed = geometry.UploadTiled(),
                LocalMin = geometry.Vertices.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min),
                LocalMax = geometry.Vertices.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max),
                Surface = SurfaceSet.Architecture, DetailedTriangles = geometry.VertexCount / 3 };
    }

    private static void AddEntrancePanel(Geometry deck, Vector3 center, Vector3 forward)
    {
        const int width = 9, height = 11;
        const float cell = .2f;
        int[,] pixels = new int[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1) pixels[x, y] = 1;
        Sector.Stamp(pixels, "D", 2, 2, 2);
        // Tag 250 adds aged paint, mineral dirt and scuffs in the world shader.
        Color[] palette = [new(133, 79, 66, 250), new(77, 61, 52, 250), new(196, 185, 151, 250)];
        Vector3 right = Vector3.Cross(forward, Vector3.UnitY);
        var footprint = new Geometry();
        var panel = new Geometry();
        void Rect(Geometry g, int x0, int y0, int x1, int y1, Color color)
        {
            Vector3 P(int x, int y) => center + right * ((x - width * .5f) * cell)
                + forward * ((height * .5f - y) * cell);
            g.Quad(P(x0, y1), P(x1, y1), P(x1, y0), P(x0, y0), color);
        }
        Rect(footprint, 0, 0, width, height, palette[0]);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width;)
            {
                int start = x, shade = pixels[x++, y];
                while (x < width && pixels[x, y] == shade) x++;
                Rect(panel, start, y, x, y + 1, palette[shade]);
            }
        // Replace the floor rather than overlaying a decal, keeping y=0 and
        // the same continuous collision surface without coplanar flickering.
        deck.Inlay(footprint, panel);
    }

    internal void SetOrigin(long x, long z)
    {
        var offset = WorldPosition.Offset(From.X, From.Z, x, z);
        Collision.Offset = offset;
        if (Chunk != null) Chunk.Offset = offset;
    }

    public void Dispose() => Chunk?.Dispose();
}
