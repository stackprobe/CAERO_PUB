using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class SurfaceChecks
{
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("Surface test failed: " + name); }

    public static void Run()
    {
        ClosedRings();
        Geometry Make(bool rotated, float offset = 0)
        {
            var mesh = new Geometry();
            Vector3 P(float x, float z, float y = 0) => rotated ? new(30 + x, -10 + z, 7 + y) : new(x, y, z);
            mesh.Quad(P(0, 0), P(0, 2), P(2, 2), P(2, 0), Color.White);
            mesh.Quad(P(1, 0, offset), P(1, 2, offset), P(3, 2, offset), P(3, 0, offset), Color.Gray);
            return mesh;
        }
        foreach (bool rotated in new[] { false, true })
        {
            var mesh = Make(rotated);
            Check(mesh.RemoveCoplanarOverlaps() > 0, "partial overlapping floor or wall detected");
            Check(Math.Abs(Area(mesh) - 6) < .0001, "union area preserved without duplicate surfaces");
            Check(mesh.HasFiniteNormals, "fragment shading normals");
            Check(mesh.RemoveCoplanarOverlaps() == 0, "cleaned surface stays free of overlap");
        }
        var separate = Make(false, .01f);
        Check(separate.RemoveCoplanarOverlaps() == 0 && Math.Abs(Area(separate) - 8) < .0001, "separate decorative layers preserved");
        foreach (bool rotated in new[] { false, true })
        {
            Vector3 P(float x, float z, float y = 0) => rotated ? new(115 + x, 51 + z, -222 + y) : new(115 + x, 51 + y, -222 + z);
            var bridge = new Geometry();
            bridge.Quad(P(0, 0), P(0, 2), P(2, 2), P(2, 0), Color.Gray);
            var terrace = new Geometry();
            terrace.Quad(P(1, 0), P(1, 2), P(3, 2), P(3, 0), Color.White);
            Check(bridge.CountCoplanarOverlaps() == 0 && terrace.CountCoplanarOverlaps() == 0,
                "individual mesh audits cannot detect a bridge/terrace overlap");
            Check(terrace.CountCoplanarOverlaps(bridge) > 0, "cross-mesh audit detects bridge/terrace overlap");
            var originalBridge = bridge.Vertices.ToArray();
            // Both detailed and coarse terrain must be trimmed against architecture.
            foreach (bool coarse in new[] { false, true })
            {
                var target = new Geometry();
                target.Quad(P(1, 0), P(1, 2), P(3, 2), P(3, 0), coarse ? Color.DarkGray : Color.White);
                Check(target.RemoveCoplanarOverlaps(occluder: bridge) > 0, "cross-mesh subtraction");
                Check(target.CountCoplanarOverlaps(bridge) == 0, "no remaining cross-mesh overlap");
                Check(Math.Abs(Area(target) + Area(bridge) - 6) < .0001, "cross-mesh union area preserved");
                Check(bridge.Vertices.SequenceEqual(originalBridge), "architectural mesh is not modified");
                Check(target.RemoveCoplanarOverlaps(occluder: bridge) == 0, "cross-mesh cleanup is stable");
                if (rotated) continue;
                var scene = new WalkableScene();
                target.AddCollisionTo(scene); bridge.AddCollisionTo(scene);
                for (float x = .1f; x < 3; x += .2f)
                    Check(Math.Abs(scene.FloorAt(115 + x, -221, 52) - 51) < .001f, "cross-mesh floor has no holes");
            }
            var raised = new Geometry();
            raised.Quad(P(1, 0, .01f), P(1, 2, .01f), P(3, 2, .01f), P(3, 0, .01f), Color.White);
            Check(raised.CountCoplanarOverlaps(bridge) == 0 && raised.RemoveCoplanarOverlaps(occluder: bridge) == 0,
                "cross-mesh cleanup preserves separate surfaces");
        }
        var duplicate = new Geometry();
        // The aqueduct approach leaves a five-millimetre strip at this height.
        // Intersections must not nudge a horizontal floor into a tilted plane.
        var clippingPlane = new CoplanarFaces();
        var clippedFace = new CoplanarFaces.Face(new(62, 14.45f, -191), new(66, 14.45f, -191.005f), new(62, 14.45f, -191.005f));
        var coverFace = new CoplanarFaces.Face(new(66, 14.45f, -191.005f), new(67.344894f, 14.45f, -193.95567f), new(57, 14.45f, -191));
        var plane = clippingPlane.GetPlane(clippedFace)!;
        var fragments = CoplanarFaces.Subtract([clippedFace.A, clippedFace.B, clippedFace.C], coverFace, plane);
        Check(fragments.SelectMany(p => p).All(p => p.Y == 14.45f), "clipping retains exact floor height on thin fragments");
        Check(fragments.All(p => !CoplanarFaces.Overlaps(p, coverFace, plane)), "thin fragments do not retain overlap");
        for (int i = 0; i < 2; i++) duplicate.Quad(new(0, 0, 0), new(0, 0, 2), new(2, 0, 2), new(2, 0, 0), Color.White);
        duplicate.RemoveCoplanarOverlaps();
        Check(Math.Abs(Area(duplicate) - 4) < .0001, "identical faces removed");
        var floor = new WalkableScene();
        duplicate.AddCollisionTo(floor);
        for (float x = .1f; x < 2; x += .2f)
            for (float z = .1f; z < 2; z += .2f)
                Check(Math.Abs(floor.FloorAt(x, z, 1)) < .001f, "no holes introduced in collision floor");
    }

    private static void ClosedRings()
    {
        foreach (int sides in new[] { 24, 64 })
        {
            var mesh = new Geometry(); mesh.Ring(Vector3.Zero, 1.6f, 2.15f, .5f, Color.Green, sides);
            var edges = new Dictionary<(string, string), (int Count, int Direction)>();
            string Key(Vector3 p) => $"{MathF.Round(p.X * 10000)},{MathF.Round(p.Y * 10000)},{MathF.Round(p.Z * 10000)}";
            int front = 0, back = 0;
            for (int i = 0; i < mesh.VertexCount; i += 3)
            {
                Vector3 a = mesh.Vertices[i], b = mesh.Vertices[i + 1], c = mesh.Vertices[i + 2];
                Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                if (a.Y == .5f && b.Y == .5f && c.Y == .5f) { Check(n.Y > .99f, "ring top faces outward"); front++; }
                if (a.Y == 0 && b.Y == 0 && c.Y == 0) { Check(n.Y < -.99f, "ring bottom faces outward"); back++; }
                foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
                {
                    string start = Key(p), end = Key(q); bool forward = string.CompareOrdinal(start, end) < 0;
                    var key = forward ? (start, end) : (end, start);
                    var old = edges.GetValueOrDefault(key);
                    edges[key] = (old.Count + 1, old.Direction + (forward ? 1 : -1));
                }
            }
            Check(front == sides * 2 && back == sides * 2, "both ring caps cover the complete circumference");
            Check(edges.Values.All(e => e.Count == 2 && e.Direction == 0), "ring has no open edges or reversed face joins");
            Check(mesh.HasFiniteNormals, "closed ring finite shading normals");
        }
        Console.WriteLine("RINGS: front/back caps, outward winding and closed mesh edges passed at 24/64 sides.");
    }

    private static double Area(Geometry mesh)
    {
        double area = 0;
        for (int i = 0; i < mesh.Vertices.Count; i += 3)
            area += Vector3.Cross(mesh.Vertices[i + 1] - mesh.Vertices[i], mesh.Vertices[i + 2] - mesh.Vertices[i]).Length() / 2;
        return area;
    }
}
