using System.Numerics;

namespace Caero;

// Keep the detailed collision surface independent of GPU meshes and visual LOD.
internal sealed class WalkableScene
{
    internal Vector3 Offset { get; set; }
    // Optional island-local geometry can be removed without rebuilding the map.
    internal WalkableScene? Additional { get; set; }
    private const float CellSize = 8;
    private readonly Dictionary<(int X, int Z), Column> cells = [];
    private sealed class Column
    {
        public readonly Dictionary<int, List<Face>> Rows = [];
        public int Min = int.MaxValue, Max = int.MinValue;
    }
    // Share tall faces between vertical buckets rather than copying their vertices.
    private sealed record Face(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, float MinY, float MaxY);
    private static int Cell(float coordinate) => (int)MathF.Floor(coordinate / CellSize);

    public void Add(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() < 1e-10f) return;
        Vector3 min = Vector3.Min(a, Vector3.Min(b, c)), max = Vector3.Max(a, Vector3.Max(b, c));
        var face = new Face(a, b, c, Vector3.Normalize(normal), min.Y, max.Y);
        int lo = Cell(min.Y), hi = Cell(max.Y);
        for (int x = Cell(min.X); x <= Cell(max.X); x++)
            for (int z = Cell(min.Z); z <= Cell(max.Z); z++)
            {
                if (!cells.TryGetValue((x, z), out var column)) cells[(x, z)] = column = new();
                column.Min = Math.Min(column.Min, lo); column.Max = Math.Max(column.Max, hi);
                for (int y = lo; y <= hi; y++)
                {
                    if (!column.Rows.TryGetValue(y, out var list)) column.Rows[y] = list = [];
                    list.Add(face);
                }
            }
    }

    public float FloorAt(float x, float z, float maximum)
    {
        x -= Offset.X; z -= Offset.Z;
        float floor = Additional?.FloorAt(x, z, maximum) ?? float.NegativeInfinity;
        if (float.IsNaN(maximum) || float.IsNegativeInfinity(maximum) || !cells.TryGetValue((Cell(x), Cell(z)), out var column)) return floor;
        int start = float.IsPositiveInfinity(maximum) ? column.Max : Math.Min(column.Max, Cell(maximum + .001f));
        for (int row = start; row >= column.Min; row--)
        {
            if (column.Rows.TryGetValue(row, out var faces))
                foreach (var face in faces)
                    if (face.Normal.Y > .01f && face.MinY <= maximum + .001f && Height(face, x, z, out float y) && y <= maximum + .001f)
                        floor = Math.Max(floor, y);
            // A sloped face can span buckets: do not stop until the actual hit
            // lies in this bucket or above it. Lower buckets cannot beat that hit.
            if (floor >= row * CellSize) break;
        }
        return floor;
    }

    public float CeilingAt(float x, float z, float minimum)
    {
        x -= Offset.X; z -= Offset.Z;
        float ceiling = Additional?.CeilingAt(x, z, minimum) ?? float.PositiveInfinity;
        if (float.IsNaN(minimum) || float.IsPositiveInfinity(minimum) || !cells.TryGetValue((Cell(x), Cell(z)), out var column)) return ceiling;
        int start = float.IsNegativeInfinity(minimum) ? column.Min : Math.Max(column.Min, Cell(minimum - .001f));
        for (int row = start; row <= column.Max; row++)
        {
            if (column.Rows.TryGetValue(row, out var faces))
                foreach (var face in faces)
                    if (face.Normal.Y < -.7f && face.MaxY >= minimum - .001f && Height(face, x, z, out float y) && y >= minimum - .001f)
                        ceiling = Math.Min(ceiling, y);
            if (ceiling <= (row + 1) * CellSize) break;
        }
        return ceiling;
    }

    private static bool Height(Face f, float x, float z, out float y)
    {
        var p = new Vector2(x - f.A.X, z - f.A.Z);
        var b = new Vector2(f.B.X - f.A.X, f.B.Z - f.A.Z);
        var c = new Vector2(f.C.X - f.A.X, f.C.Z - f.A.Z);
        float determinant = b.X * c.Y - b.Y * c.X;
        float u = (p.X * c.Y - p.Y * c.X) / determinant;
        float v = (b.X * p.Y - b.Y * p.X) / determinant;
        y = f.A.Y + u * (f.B.Y - f.A.Y) + v * (f.C.Y - f.A.Y);
        return u >= -.0001f && v >= -.0001f && u + v <= 1.0001f;
    }

    public bool Obstructs(Vector3 from, Vector3 to)
    {
        from -= Offset; to -= Offset;
        if (Additional?.Obstructs(from, to) == true) return true;
        var lo = Vector3.Min(from, to); var hi = Vector3.Max(from, to);
        for (int x = Cell(lo.X); x <= Cell(hi.X); x++)
            for (int z = Cell(lo.Z); z <= Cell(hi.Z); z++)
                if (cells.TryGetValue((x, z), out var column))
                    for (int y = Cell(lo.Y); y <= Cell(hi.Y); y++)
                        if (column.Rows.TryGetValue(y, out var faces))
                            foreach (var f in faces)
                                if (SegmentDistanceSquared(from, to, f) < 1e-8f) return true;
        return false;
    }
    internal bool SphereBlocked(Vector3 center, float radius)
    {
        center -= Offset;
        if (Additional?.SphereBlocked(center, radius) == true) return true;
        for (int x = Cell(center.X - radius); x <= Cell(center.X + radius); x++)
            for (int z = Cell(center.Z - radius); z <= Cell(center.Z + radius); z++)
                if (cells.TryGetValue((x, z), out var column))
                    for (int y = Cell(center.Y - radius); y <= Cell(center.Y + radius); y++)
                        if (column.Rows.TryGetValue(y, out var faces))
                            foreach (var face in faces)
                                if (PointDistanceSquared(center, face) < radius * radius) return true;
        return false;
    }
    // Exact triangle/AABB separating-axis test, including thin walls, beams and stairs.
    internal bool SurfaceOverlapsBox(Vector3 min, Vector3 max)
    {
        min -= Offset; max -= Offset;
        if (Additional?.SurfaceOverlapsBox(min, max) == true) return true;
        var visited = new HashSet<Face>();
        for (int x = Cell(min.X); x <= Cell(max.X); x++)
            for (int z = Cell(min.Z); z <= Cell(max.Z); z++)
                if (cells.TryGetValue((x, z), out var column))
                    for (int y = Cell(min.Y); y <= Cell(max.Y); y++)
                        if (column.Rows.TryGetValue(y, out var faces))
                            foreach (var face in faces)
                                if (visited.Add(face) && TriangleOverlapsBox(face, min, max)) return true;
        return false;
    }
    private static bool TriangleOverlapsBox(Face f, Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * .5f, half = (max - min) * .5f;
        Vector3 a = f.A - center, b = f.B - center, c = f.C - center;
        bool Separates(Vector3 axis)
        {
            float p = Vector3.Dot(a, axis), q = Vector3.Dot(b, axis), r = Vector3.Dot(c, axis);
            float extent = Vector3.Dot(half, Vector3.Abs(axis));
            return Math.Min(p, Math.Min(q, r)) > extent || Math.Max(p, Math.Max(q, r)) < -extent;
        }
        if (Separates(Vector3.UnitX) || Separates(Vector3.UnitY) || Separates(Vector3.UnitZ) || Separates(f.Normal)) return false;
        foreach (var edge in new[] { b - a, c - b, a - c })
            if (Separates(Vector3.Cross(edge, Vector3.UnitX)) || Separates(Vector3.Cross(edge, Vector3.UnitY))
                || Separates(Vector3.Cross(edge, Vector3.UnitZ))) return false;
        return true;
    }
    public bool Blocked(Vector3 eye)
    {
        eye -= Offset;
        if (Additional?.Blocked(eye) == true) return true;
        const float radius = .30f;
        Vector3 bottom = eye - Vector3.UnitY * (Explorer.EyeHeight - radius - .02f);
        Vector3 top = eye - Vector3.UnitY * radius;
        for (int x = Cell(eye.X - radius); x <= Cell(eye.X + radius); x++)
            for (int z = Cell(eye.Z - radius); z <= Cell(eye.Z + radius); z++)
            {
                if (!cells.TryGetValue((x, z), out var column)) continue;
                for (int y = Cell(bottom.Y - radius); y <= Cell(top.Y + radius); y++)
                {
                    if (!column.Rows.TryGetValue(y, out var faces)) continue;
                    foreach (var f in faces)
                    {
                        if (f.MaxY < bottom.Y - radius || f.MinY > top.Y + radius) continue;
                        if (SegmentDistanceSquared(bottom, top, f) < radius * radius - .0001f) return true;
                    }
                }
            }
        return false;
    }

    private static float SegmentDistanceSquared(Vector3 a, Vector3 b, Face f)
    {
        Vector3 ab = b - a;
        float denominator = Vector3.Dot(f.Normal, ab);
        if (Math.Abs(denominator) > 1e-6f)
        {
            float t = Vector3.Dot(f.Normal, f.A - a) / denominator;
            if (t >= 0 && t <= 1 && PointDistanceSquared(a + ab * t, f) < 1e-8f) return 0;
        }
        return Math.Min(Math.Min(PointDistanceSquared(a, f), PointDistanceSquared(b, f)),
            Math.Min(EdgeDistanceSquared(a, b, f.A, f.B),
                Math.Min(EdgeDistanceSquared(a, b, f.B, f.C), EdgeDistanceSquared(a, b, f.C, f.A))));
    }

    private static float PointDistanceSquared(Vector3 p, Face f)
    {
        Vector3 ab = f.B - f.A, ac = f.C - f.A, ap = p - f.A;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return ap.LengthSquared();
        Vector3 bp = p - f.B;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return bp.LengthSquared();
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return Vector3.DistanceSquared(p, f.A + ab * (d1 / (d1 - d3)));
        Vector3 cp = p - f.C;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return cp.LengthSquared();
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return Vector3.DistanceSquared(p, f.A + ac * (d2 / (d2 - d6)));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return Vector3.DistanceSquared(p, f.B + (f.C - f.B) * ((d4 - d3) / (d4 - d3 + d5 - d6)));
        float distance = Vector3.Dot(ap, f.Normal);
        return distance * distance;
    }

    private static float EdgeDistanceSquared(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
    {
        Vector3 d = q - p, e = b - a, r = p - a;
        float dd = Vector3.Dot(d, d), ee = Vector3.Dot(e, e), de = Vector3.Dot(d, e);
        float dr = Vector3.Dot(d, r), er = Vector3.Dot(e, r), divisor = dd * ee - de * de;
        float s = divisor > 1e-10f ? Math.Clamp((de * er - dr * ee) / divisor, 0, 1) : 0;
        float t = (de * s + er) / ee;
        if (t < 0) { t = 0; s = Math.Clamp(-dr / dd, 0, 1); }
        else if (t > 1) { t = 1; s = Math.Clamp((de - dr) / dd, 0, 1); }
        return (r + d * s - e * t).LengthSquared();
    }
}
