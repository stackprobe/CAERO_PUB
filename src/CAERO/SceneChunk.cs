using System.Numerics;
using Raylib_cs;

namespace Caero;

internal enum SurfaceSet { Architecture, Landscape }

internal sealed class SceneChunk : IDisposable
{
    public Model Detailed;
    public Vector3 LocalMin, LocalMax, Offset;
    public Vector3 MaterialOffset;
    public Vector3 Min => LocalMin + Offset;
    public Vector3 Max => LocalMax + Offset;
    public SurfaceSet Surface;
    public Vector3 WallFinish;
    public float WallStrength;
    public bool PrimeNumber;
    public int DetailedTriangles;
    public float SortDistanceSquared;
    public float DistanceSquared(Vector3 eye) => Vector3.DistanceSquared(eye, Vector3.Clamp(eye, Min, Max));
    public void Dispose() => Raylib.UnloadModel(Detailed);
}

// Conservative AABB tests preserve objects crossing screen and distance boundaries.
internal sealed class SceneVisibility
{
    public const float DrawDistance = 240;
    private readonly Vector3[] planes = new Vector3[5];
    private Vector3 eye;
    private float distance = DrawDistance;
    public void Update(Camera3D camera, float aspect, float drawDistance = DrawDistance)
    {
        eye = camera.Position; distance = drawDistance;
        Vector3 forward = Vector3.Normalize(camera.Target - eye);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        Vector3 up = Vector3.Cross(right, forward);
        float vertical = camera.FovY * MathF.PI / 360;
        float horizontal = MathF.Atan(MathF.Tan(vertical) * aspect);
        planes[0] = forward;
        planes[1] = forward * MathF.Sin(horizontal) + right * MathF.Cos(horizontal);
        planes[2] = forward * MathF.Sin(horizontal) - right * MathF.Cos(horizontal);
        planes[3] = forward * MathF.Sin(vertical) + up * MathF.Cos(vertical);
        planes[4] = forward * MathF.Sin(vertical) - up * MathF.Cos(vertical);
    }
    public bool Visible(Vector3 min, Vector3 max)
    {
        if (Vector3.DistanceSquared(eye, Vector3.Clamp(eye, min, max)) > distance * distance) return false;
        Vector3 center = (min + max) * .5f - eye, half = (max - min) * .5f;
        foreach (var normal in planes)
        {
            float radius = Vector3.Dot(half, Vector3.Abs(normal));
            if (Vector3.Dot(center, normal) + radius < -.01f) return false;
        }
        return true;
    }
}
