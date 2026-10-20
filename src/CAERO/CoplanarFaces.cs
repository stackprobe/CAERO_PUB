using System.Numerics;

namespace Caero;

// Subtract later surfaces from earlier coplanar surfaces before baking or collision.
// This also handles rotated walls, rather than relying on offsets or draw order.
internal sealed class CoplanarFaces
{
    internal readonly record struct Face(Vector3 A, Vector3 B, Vector3 C);
    private readonly Dictionary<(int, int, int, int), Plane> planes = [];
    private const float Epsilon = .00001f;

    internal sealed class Plane(int axis)
    {
        private readonly List<Face> faces = [];
        private readonly Dictionary<(int, int), List<int>> cells = [];
        private readonly List<int> large = [];
        public Vector2 Project(Vector3 p) => axis == 0 ? new(p.Y, p.Z) : axis == 1 ? new(p.X, p.Z) : new(p.X, p.Y);
        private static int Cell(float value) => (int)MathF.Floor(value / 4);
        private (int X0, int X1, int Y0, int Y1) Bounds(Face f)
        {
            var min = Vector2.Min(Project(f.A), Vector2.Min(Project(f.B), Project(f.C)));
            var max = Vector2.Max(Project(f.A), Vector2.Max(Project(f.B), Project(f.C)));
            return (Cell(min.X), Cell(max.X), Cell(min.Y), Cell(max.Y));
        }
        public IEnumerable<Face> Candidates(Face f)
        {
            var b = Bounds(f);
            if ((long)(b.X1 - b.X0 + 1) * (b.Y1 - b.Y0 + 1) > 4096)
            {
                foreach (var face in faces) yield return face;
                yield break;
            }
            var seen = new HashSet<int>();
            foreach (int id in large) { seen.Add(id); yield return faces[id]; }
            for (int x = b.X0; x <= b.X1; x++)
                for (int y = b.Y0; y <= b.Y1; y++)
                    if (cells.TryGetValue((x, y), out var ids))
                        foreach (int id in ids)
                            if (seen.Add(id)) yield return faces[id];
        }
        public void Add(Face f)
        {
            int id = faces.Count; faces.Add(f);
            var b = Bounds(f);
            if ((long)(b.X1 - b.X0 + 1) * (b.Y1 - b.Y0 + 1) > 4096) { large.Add(id); return; }
            for (int x = b.X0; x <= b.X1; x++)
                for (int y = b.Y0; y <= b.Y1; y++)
                {
                    if (!cells.TryGetValue((x, y), out var ids)) cells[(x, y)] = ids = [];
                    ids.Add(id);
                }
        }
    }

    public Plane? GetPlane(Face f)
    {
        Vector3 n = Vector3.Cross(f.B - f.A, f.C - f.A);
        if (n.LengthSquared() < 1e-12f) return null;
        n = Vector3.Normalize(n);
        // Bucket by a stable canonical normal. Tiny fragments otherwise acquire
        // slightly different floating-point plane keys after triangulation.
        int nx = (int)MathF.Round(n.X * 1000), ny = (int)MathF.Round(n.Y * 1000), nz = (int)MathF.Round(n.Z * 1000);
        Vector3 canonical = Vector3.Normalize(new Vector3(nx, ny, nz));
        var key = (nx, ny, nz, (int)MathF.Round(Vector3.Dot(canonical, f.A) * 10));
        if (!planes.TryGetValue(key, out var plane))
        {
            Vector3 abs = Vector3.Abs(n);
            int axis = abs.X > abs.Y && abs.X > abs.Z ? 0 : abs.Y > abs.Z ? 1 : 2;
            planes[key] = plane = new(axis);
        }
        return plane;
    }

    private static double Turn(Vector2 a, Vector2 b, Vector2 p) =>
        ((double)b.X - a.X) * ((double)p.Y - a.Y) - ((double)b.Y - a.Y) * ((double)p.X - a.X);
    private static List<Vector3> Clip(List<Vector3> polygon, Vector2 a, Vector2 b, float sign, bool inside, Plane plane)
    {
        var output = new List<Vector3>();
        if (polygon.Count == 0) return output;
        Vector3 previous = polygon[^1];
        double before = sign * Turn(a, b, plane.Project(previous));
        foreach (var current in polygon)
        {
            double distance = sign * Turn(a, b, plane.Project(current));
            bool keep = inside ? distance >= 0 : distance <= 0;
            bool keptBefore = inside ? before >= 0 : before <= 0;
            if (keep != keptBefore)
                // Lerp's weighted sum can change a constant height by one float
                // ULP. On a thin fragment that tilts its plane enough to evade
                // later cross-mesh subtraction. A zero delta stays exactly zero.
                output.Add(previous + (current - previous) * (float)(before / (before - distance)));
            if (keep) output.Add(current);
            previous = current; before = distance;
        }
        return output;
    }

    private static double Area(List<Vector3> polygon, Plane plane)
    {
        double area = 0;
        for (int i = 1; i + 1 < polygon.Count; i++)
            area += Math.Abs(Turn(plane.Project(polygon[0]), plane.Project(polygon[i]), plane.Project(polygon[i + 1]))) / 2;
        return area;
    }

    public static bool Overlaps(List<Vector3> polygon, Face cover, Plane plane)
    {
        Vector3 normal = Vector3.Normalize(Vector3.Cross(cover.B - cover.A, cover.C - cover.A));
        if (polygon.Any(p => Math.Abs(Vector3.Dot(normal, p - cover.A)) > .0001f)) return false;
        Vector2[] clip = [plane.Project(cover.A), plane.Project(cover.B), plane.Project(cover.C)];
        float sign = Math.Sign(Turn(clip[0], clip[1], clip[2]));
        var intersection = polygon;
        for (int i = 0; i < 3 && intersection.Count > 0; i++)
            intersection = Clip(intersection, clip[i], clip[(i + 1) % 3], sign, true, plane);
        double area = Area(intersection, plane);
        if (area <= Epsilon) return false;
        double longest = 0;
        for (int i = 0; i < intersection.Count; i++)
            for (int j = i + 1; j < intersection.Count; j++)
                longest = Math.Max(longest, Vector2.Distance(plane.Project(intersection[i]), plane.Project(intersection[j])));
        // Float vertices can place a shared edge a few micrometers across its neighbor.
        // Treat strips narrower than 0.1 mm as the same edge, rather than real overlap.
        return longest > 0 && area * 2 / longest >= .0001;
    }

    public static List<List<Vector3>> Subtract(List<Vector3> polygon, Face cover, Plane plane)
    {
        if (!Overlaps(polygon, cover, plane)) return [polygon];
        Vector2[] clip = [plane.Project(cover.A), plane.Project(cover.B), plane.Project(cover.C)];
        float sign = Math.Sign(Turn(clip[0], clip[1], clip[2]));
        var result = new List<List<Vector3>>();
        for (int i = 0; i < 3 && polygon.Count > 0; i++)
        {
            var outside = Clip(polygon, clip[i], clip[(i + 1) % 3], sign, false, plane);
            if (Area(outside, plane) > Epsilon) result.Add(outside);
            polygon = Clip(polygon, clip[i], clip[(i + 1) % 3], sign, true, plane);
        }
        return result;
    }
}
