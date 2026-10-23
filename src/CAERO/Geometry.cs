using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed class Geometry
{
    private readonly List<Vector3> positions = [], normals = [];
    private readonly List<Color> colors = [];
    public int VertexCount => positions.Count;
    internal IReadOnlyList<Vector3> Vertices => positions;
    internal bool HasFiniteNormals => normals.All(n => float.IsFinite(n.X) && float.IsFinite(n.Y) && float.IsFinite(n.Z));
    internal int CountCoplanarOverlaps(int first = 0, int? count = null)
    {
        var index = new CoplanarFaces();
        int overlaps = 0;
        int end = count.HasValue ? first + count.Value : positions.Count;
        for (int i = first; i < end; i += 3)
        {
            var face = new CoplanarFaces.Face(positions[i], positions[i + 1], positions[i + 2]);
            var plane = index.GetPlane(face);
            if (plane == null) continue;
            foreach (var cover in plane.Candidates(face))
                if (CoplanarFaces.Overlaps([face.A, face.B, face.C], cover, plane))
                {
                    overlaps++;
                }
            plane.Add(face);
        }
        return overlaps;
    }
    internal int CountCoplanarOverlaps(Geometry other, bool report = false)
    {
        var index = other.SurfaceIndex();
        int count = 0;
        for (int i = 0; i < positions.Count; i += 3)
        {
            var face = new CoplanarFaces.Face(positions[i], positions[i + 1], positions[i + 2]);
            var plane = index.GetPlane(face);
            if (plane == null) continue;
            foreach (var cover in plane.Candidates(face))
                if (CoplanarFaces.Overlaps([face.A, face.B, face.C], cover, plane))
                {
                    if (report && count < 5) Console.WriteLine($"CROSS-MESH OVERLAP: {face} / {cover}");
                    count++;
                }
        }
        return count;
    }
    private CoplanarFaces SurfaceIndex()
    {
        var index = new CoplanarFaces();
        for (int i = 0; i < positions.Count; i += 3)
        {
            var face = new CoplanarFaces.Face(positions[i], positions[i + 1], positions[i + 2]);
            index.GetPlane(face)?.Add(face);
        }
        return index;
    }
    public int RemoveCoplanarOverlaps(int first = 0, Geometry? occluder = null)
    {
        if (ReferenceEquals(this, occluder)) throw new ArgumentException("Occluder must be a separate mesh.", nameof(occluder));
        int total = 0;
        // Revisit triangulated fragments to resolve plane keys affected by rounding.
        for (int pass = 0; pass < 8; pass++)
        {
            int removed = RemoveCoplanarPass(first, occluder);
            total += removed;
            if (removed == 0) break;
        }
        return total;
    }
    // Replace a patch of an existing surface without changing unrelated faces.
    internal int[] Inlay(Geometry footprint, Geometry surface)
    {
        var remap = new int[positions.Count / 3 + 1];
        RemoveCoplanarPass(0, footprint, false, remap);
        Append(surface);
        return remap;
    }
    private int RemoveCoplanarPass(int first, Geometry? occluder, bool resolveSelf = true, int[]? remap = null)
    {
        // A material/LOD boundary is not a geometric boundary. Keep the other
        // mesh intact and subtract its faces before resolving this mesh's faces.
        var index = occluder?.SurfaceIndex() ?? new CoplanarFaces();
        var vertices = positions.Take(first).ToList();
        var shading = normals.Take(first).ToList();
        var tints = colors.Take(first).ToList();
        int overlaps = 0;
        for (int step = 0; step < (positions.Count - first) / 3; step++)
        {
            int i = resolveSelf ? positions.Count - 3 - step * 3 : first + step * 3;
            if (remap != null) remap[i / 3] = vertices.Count;
            var face = new CoplanarFaces.Face(positions[i], positions[i + 1], positions[i + 2]);
            var plane = index.GetPlane(face);
            if (plane == null) continue;
            List<List<Vector3>> pieces = [[face.A, face.B, face.C]];
            foreach (var cover in plane.Candidates(face))
            {
                var remainder = new List<List<Vector3>>();
                foreach (var piece in pieces)
                {
                    var split = CoplanarFaces.Subtract(piece, cover, plane);
                    if (split.Count != 1 || !ReferenceEquals(split[0], piece)) overlaps++;
                    remainder.AddRange(split);
                }
                pieces = remainder;
                if (pieces.Count == 0) break;
            }
            if (resolveSelf) plane.Add(face);
            if (!resolveSelf && pieces.Count == 1 && pieces[0].Count == 3
                && pieces[0][0] == face.A && pieces[0][1] == face.B && pieces[0][2] == face.C)
            {
                vertices.AddRange([face.A, face.B, face.C]);
                shading.AddRange([normals[i], normals[i + 1], normals[i + 2]]);
                tints.AddRange([colors[i], colors[i + 1], colors[i + 2]]);
                continue;
            }
            Vector3 ab = face.B - face.A, ac = face.C - face.A;
            static double Dot(Vector3 a, Vector3 b) => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
            double d00 = Dot(ab, ab), d01 = Dot(ab, ac), d11 = Dot(ac, ac);
            double determinant = d00 * d11 - d01 * d01;
            Vector3 Normal(Vector3 p)
            {
                Vector3 ap = p - face.A;
                if (determinant <= 0) return Vector3.Normalize(Vector3.Cross(ab, ac));
                double d20 = Dot(ap, ab), d21 = Dot(ap, ac);
                float u = (float)((d11 * d20 - d01 * d21) / determinant);
                float v = (float)((d00 * d21 - d01 * d20) / determinant);
                return Vector3.Normalize(normals[i] * (1 - u - v) + normals[i + 1] * u + normals[i + 2] * v);
            }
            foreach (var piece in pieces)
                for (int j = 1; j + 1 < piece.Count; j++)
                {
                    Vector3 a = piece[0], b = piece[j], c = piece[j + 1];
                    float twiceArea = Vector3.Cross(b - a, c - a).Length();
                    float longest = MathF.Sqrt(Math.Max(Vector3.DistanceSquared(a, b),
                        Math.Max(Vector3.DistanceSquared(b, c), Vector3.DistanceSquared(c, a))));
                    // Discard sub-millimeter clipping slivers caused by float vertex storage.
                    if (twiceArea < 1e-5f || twiceArea / longest < .0001f) continue;
                    vertices.AddRange([a, b, c]);
                    shading.AddRange([Normal(a), Normal(b), Normal(c)]);
                    tints.AddRange([colors[i], colors[i], colors[i]]);
                }
        }
        if (remap != null) remap[^1] = vertices.Count;
        positions.Clear(); positions.AddRange(vertices);
        normals.Clear(); normals.AddRange(shading);
        colors.Clear(); colors.AddRange(tints);
        return overlaps;
    }
    public void AddCollisionTo(WalkableScene scene)
    {
        for (int i = 0; i < positions.Count; i += 3)
        {
            byte tag = colors[i].A;
            // Water is a swimming volume; leaves and grass are passable.
            if (tag < 120 || (tag >= 165 && tag < 200)) continue;
            scene.Add(positions[i], positions[i + 1], positions[i + 2]);
        }
    }
    public void Clear()
    {
        positions.Clear(); positions.TrimExcess(); normals.Clear(); normals.TrimExcess(); colors.Clear(); colors.TrimExcess();
    }
    public void Append(Geometry source)
    {
        positions.AddRange(source.positions); normals.AddRange(source.normals); colors.AddRange(source.colors);
    }
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        positions.AddRange([a, b, c]); normals.AddRange([normal, normal, normal]);
        colors.AddRange([color, color, color]);
    }
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    { Triangle(a, b, c, color); Triangle(a, c, d, color); }
    internal void SmoothTriangle(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 na, Vector3 nb, Vector3 nc)
    {
        positions.AddRange([a, b, c]); normals.AddRange([na, nb, nc]); colors.AddRange([color, color, color]);
    }
    public void Box(Vector3 center, Vector3 size, Color color, float yaw = 0)
    {
        Vector3 lo = center - size / 2, hi = center + size / 2;
        Vector3 P(float x, float y, float z)
        {
            Vector3 v = new(x - center.X, y - center.Y, z - center.Z);
            return center + new Vector3(v.X * MathF.Cos(yaw) + v.Z * MathF.Sin(yaw), v.Y,
                -v.X * MathF.Sin(yaw) + v.Z * MathF.Cos(yaw));
        }
        Quad(P(lo.X, lo.Y, hi.Z), P(hi.X, lo.Y, hi.Z), P(hi.X, hi.Y, hi.Z), P(lo.X, hi.Y, hi.Z), color);
        Quad(P(hi.X, lo.Y, lo.Z), P(lo.X, lo.Y, lo.Z), P(lo.X, hi.Y, lo.Z), P(hi.X, hi.Y, lo.Z), color);
        Quad(P(lo.X, lo.Y, lo.Z), P(lo.X, lo.Y, hi.Z), P(lo.X, hi.Y, hi.Z), P(lo.X, hi.Y, lo.Z), color);
        Quad(P(hi.X, lo.Y, hi.Z), P(hi.X, lo.Y, lo.Z), P(hi.X, hi.Y, lo.Z), P(hi.X, hi.Y, hi.Z), color);
        Quad(P(lo.X, hi.Y, hi.Z), P(hi.X, hi.Y, hi.Z), P(hi.X, hi.Y, lo.Z), P(lo.X, hi.Y, lo.Z), color);
        Quad(P(lo.X, lo.Y, lo.Z), P(hi.X, lo.Y, lo.Z), P(hi.X, lo.Y, hi.Z), P(lo.X, lo.Y, hi.Z), color);
    }
    public void Cylinder(Vector3 bottom, float radius, float topRadius, float height, Color color, int sides = 12, float rotation = 0, bool bottomCap = true)
    {
        for (int i = 0; i < sides; i++)
        {
            float a = i * MathF.Tau / sides + rotation, b = (i + 1) * MathF.Tau / sides + rotation;
            Vector3 p = bottom + new Vector3(MathF.Cos(a) * radius, 0, MathF.Sin(a) * radius);
            Vector3 q = bottom + new Vector3(MathF.Cos(b) * radius, 0, MathF.Sin(b) * radius);
            Vector3 r = bottom + new Vector3(MathF.Cos(b) * topRadius, height, MathF.Sin(b) * topRadius);
            Vector3 s = bottom + new Vector3(MathF.Cos(a) * topRadius, height, MathF.Sin(a) * topRadius);
            Quad(q, p, s, r, color);
            Triangle(bottom + Vector3.UnitY * height, r, s, color);
            if (bottomCap) Triangle(bottom, p, q, color);
        }
    }
    public void Arch(Vector3 center, float inner, float thickness, float depth, Color color, bool alongZ = false)
    {
        Vector3 P(float r, float a, float z)
        {
            var v = new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, z);
            if (alongZ) v = new Vector3(-v.Z, v.Y, v.X);
            return center + v;
        }
        const int count = 20;
        for (int i = 0; i < count; i++)
        {
            float a = i * MathF.PI / count, b = (i + 1) * MathF.PI / count;
            float outer = inner + thickness;
            Vector3 p = P(inner, a, depth / 2), q = P(outer, a, depth / 2),
                r = P(outer, b, depth / 2), s = P(inner, b, depth / 2);
            Vector3 p2 = P(inner, a, -depth / 2), q2 = P(outer, a, -depth / 2),
                r2 = P(outer, b, -depth / 2), s2 = P(inner, b, -depth / 2);
            var tint = new Color((byte)(color.R - i % 3 * 3), (byte)(color.G - i % 3 * 3), (byte)(color.B - i % 3 * 3), color.A);
            Quad(p, q, r, s, tint); Quad(s2, r2, q2, p2, tint);
            Quad(q, q2, r2, r, tint); Quad(s, s2, p2, p, tint);
            Quad(p2, q2, q, p, tint); Quad(s, r, r2, s2, tint);
        }
    }
    // Masonry above the curved arch connects its voussoirs to the wall cornice.
    public void Spandrel(Vector3 center, float radius, float top, float depth, Color color, bool alongZ = false)
    {
        Vector3 P(float x, float y, float z) => center + (alongZ ? new Vector3(-z, y, x) : new Vector3(x, y, z));
        for (int i = 0; i < 24; i++)
        {
            float a = i * MathF.PI / 24, b = (i + 1) * MathF.PI / 24;
            float x0 = MathF.Cos(a) * radius, x1 = MathF.Cos(b) * radius;
            float y0 = MathF.Sin(a) * radius, y1 = MathF.Sin(b) * radius;
            Quad(P(x0, y0, depth / 2), P(x0, top, depth / 2), P(x1, top, depth / 2), P(x1, y1, depth / 2), color);
            Quad(P(x1, y1, -depth / 2), P(x1, top, -depth / 2), P(x0, top, -depth / 2), P(x0, y0, -depth / 2), color);
        }
    }
    public void Ring(Vector3 center, float inner, float outer, float height, Color color, int sides = 64)
    {
        Vector3 P(float r, float a, float y) => center + new Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r);
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides, b = MathF.Tau * (i + 1) / sides;
            Quad(P(outer, b, 0), P(outer, a, 0), P(outer, a, height), P(outer, b, height), color);
            Quad(P(inner, a, 0), P(inner, b, 0), P(inner, b, height), P(inner, a, height), color);
            Quad(P(inner, a, height), P(inner, b, height), P(outer, b, height), P(outer, a, height), color);
            Quad(P(inner, a, 0), P(outer, a, 0), P(outer, b, 0), P(inner, b, 0), color);
        }
    }
    public void Ellipsoid(Vector3 center, Vector3 radius, Color color, int sides = 9, int rings = 5)
    {
        Vector3 P(float a, float b) => center + radius * new Vector3(MathF.Sin(b) * MathF.Cos(a), MathF.Cos(b), MathF.Sin(b) * MathF.Sin(a));
        Vector3 N(Vector3 p) => Vector3.Normalize((p - center) / (radius * radius));
        void T(Vector3 a, Vector3 b, Vector3 c) => SmoothTriangle(a, b, c, color, N(a), N(b), N(c));
        for (int row = 0; row < rings; row++)
            for (int col = 0; col < sides; col++)
            {
                float a = col * MathF.Tau / sides, b = (col + 1) * MathF.Tau / sides;
                float u = row * MathF.PI / rings, v = (row + 1) * MathF.PI / rings;
                if (row > 0) T(P(a, u), P(b, u), P(a, v));
                if (row < rings - 1) T(P(b, u), P(b, v), P(a, v));
            }
    }
    public void Cliff(Vector3 center, Vector2 radius, float height, int seed, Color color, int sides = 49, int rings = 9,
        float? floor = null)
    {
        Vector3 P(int col, int row)
        {
            float a = col * MathF.Tau / sides, t = row / (float)rings;
            float ripple = 1 + .13f * MathF.Sin(a * 3 + seed) + .08f * MathF.Sin(a * 7 - t * 9 + seed);
            float taper = (1.18f - t * .40f) * ripple;
            float y = t * height + MathF.Sin(a * 4 + seed) * 2.5f * t;
            return center + new Vector3(MathF.Cos(a) * radius.X * taper, y, MathF.Sin(a) * radius.Y * taper);
        }
        var points = new Vector3[(rings + 1) * sides];
        var smoothNormals = new Vector3[points.Length];
        int Index(int col, int row) => row * sides + col % sides;
        for (int row = 0; row <= rings; row++)
            for (int col = 0; col < sides; col++) points[Index(col, row)] = P(col, row);
        void Accumulate(int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
            smoothNormals[a] += face; smoothNormals[b] += face; smoothNormals[c] += face;
        }
        for (int row = 0; row < rings; row++)
            for (int col = 0; col < sides; col++)
            {
                int q = Index(col + 1, row), p = Index(col, row), s = Index(col, row + 1), r = Index(col + 1, row + 1);
                Accumulate(q, p, s); Accumulate(q, s, r);
            }
        for (int i = 0; i < smoothNormals.Length; i++) smoothNormals[i] = Vector3.Normalize(smoothNormals[i]);
        void T(int a, int b, int c)
        {
            Vector3 face = Vector3.Normalize(Vector3.Cross(points[b] - points[a], points[c] - points[a]));
            Vector3 N(int i) => Vector3.Normalize(smoothNormals[i] * .82f + face * .18f);
            SmoothTriangle(points[a], points[b], points[c], color, N(a), N(b), N(c));
        }
        for (int row = 0; row < rings; row++)
            for (int col = 0; col < sides; col++)
            {
                int q = Index(col + 1, row), p = Index(col, row), s = Index(col, row + 1), r = Index(col + 1, row + 1);
                T(q, p, s); T(q, s, r);
                if (row == rings - 1) Triangle(center + new Vector3(0, height, 0), points[r], points[s], color);
            }
        if (floor is float bottom && bottom < center.Y)
        {
            // Continue the exact existing perimeter; changing height/rings would also
            // reshape the shoreline and the walkable rock above it.
            Vector3 Foot(Vector3 p) => new(center.X + (p.X - center.X) * 1.08f,
                bottom, center.Z + (p.Z - center.Z) * 1.08f);
            for (int col = 0; col < sides; col++)
            {
                Vector3 p = points[Index(col, 0)], q = points[Index(col + 1, 0)];
                Quad(Foot(q), Foot(p), p, q, color);
            }
        }
    }
    public void Transform(Func<Vector3, Vector3> rotate, Vector3 offset)
    {
        for (int i = 0; i < positions.Count; i++)
        { positions[i] = rotate(positions[i]) + offset; normals[i] = rotate(normals[i]); }
    }
    public Model UploadTiled() => UploadMesh(new Vector2[positions.Count]);
    internal Model UploadMapped(Func<Vector3, Vector2> coordinates) => UploadMesh(positions.Select(coordinates).ToArray());
    private unsafe Model UploadMesh(IReadOnlyList<Vector2> coordinates)
    {
        int count = positions.Count;
        Mesh mesh = new() { VertexCount = count, TriangleCount = count / 3 };
        mesh.Vertices = (float*)Raylib.MemAlloc((uint)(count * 3 * sizeof(float)));
        mesh.Normals = (float*)Raylib.MemAlloc((uint)(count * 3 * sizeof(float)));
        mesh.TexCoords = (float*)Raylib.MemAlloc((uint)(count * 2 * sizeof(float)));
        mesh.Colors = (byte*)Raylib.MemAlloc((uint)(count * 4));
        for (int i = 0; i < count; i++)
        {
            mesh.Vertices[i * 3] = positions[i].X; mesh.Vertices[i * 3 + 1] = positions[i].Y; mesh.Vertices[i * 3 + 2] = positions[i].Z;
            mesh.Normals[i * 3] = normals[i].X; mesh.Normals[i * 3 + 1] = normals[i].Y; mesh.Normals[i * 3 + 2] = normals[i].Z;
            mesh.TexCoords[i * 2] = coordinates[i].X; mesh.TexCoords[i * 2 + 1] = coordinates[i].Y;
            mesh.Colors[i * 4] = colors[i].R; mesh.Colors[i * 4 + 1] = colors[i].G;
            // Alpha is a surface-class tag; the world shader still outputs opacity 1.
            mesh.Colors[i * 4 + 2] = colors[i].B; mesh.Colors[i * 4 + 3] = colors[i].A;
        }
        Raylib.UploadMesh(ref mesh, false);
        return Raylib.LoadModelFromMesh(mesh);
    }
}
