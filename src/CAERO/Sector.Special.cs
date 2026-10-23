using System.Numerics;
using Raylib_cs;

namespace Caero;

internal sealed partial class Sector
{
    internal List<SpecialObject> SpecialObjects { get; } = [];
    internal IEnumerable<Resident> ProceduralPeople => People.Where(p => p.SpecialId == null);

    // This final pass adds authored props/people and the exit island's corridor.
    // The original map meshes, random
    // streams, lamps, signs and procedural residents have already been built.
    private void BuildSpecialContent()
    {
        var occupied = new List<(Vector3 Foot, float Radius)>();
        foreach (var d in SpecialContent.ForIsland(IslandNumber))
        {
            bool exitAlcove = d.Kind == SpecialKind.Exit && d.Surface == SpecialSurface.TowerLanding && d.Landing == 1;
            var pedestal = exitAlcove ? ExitRelic.BuildAlcoveFloor() : new Geometry();
            if (exitAlcove)
            {
                // Only the island with this configured exit receives the floor.
                // Keep it separate from the shared tower meshes and random stream.
                pedestal.Transform(Rotate, Vector3.Zero);
                pedestal.RemoveCoplanarOverlaps(occluder: Stone);
                pedestal.AddCollisionTo(Collision);
            }
            bool corner = d.Surface == SpecialSurface.CornerPlatform;
            float height = d.Surface switch { SpecialSurface.HallRoof => HallHeight,
                SpecialSurface.TowerLanding => d.Landing * 6,
                SpecialSurface.TowerRoof => TowerFloors * 12, SpecialSurface.CornerPlatform => 2, _ => 0 };
            Vector3 requested = new(d.Position[0], height + d.Position[1], d.Position[2]);
            float yaw = d.YawDegrees * MathF.PI / 180;
            // The four corner platforms are symmetric. Keep their authored
            // placement and gaze in compass directions as buildings rotate.
            if (corner) { requested = ResidentLocal(requested + Offset); yaw += Rotation * MathF.PI / 2; }
            float scale = d.Age == ResidentAge.Child ? .68f : d.Gender == "female" ? .96f : 1.04f;
            bool resident = d.Kind == SpecialKind.Resident;
            Vector2 half = resident ? new(.8f * scale) : d.Kind == SpecialKind.Item ? new(.5f) : new(.95f);
            var rejected = new Dictionary<string, int>();
            bool Reject(string reason) { rejected[reason] = rejected.GetValueOrDefault(reason) + 1; return false; }
            bool WithinSurface(Vector3 p) => d.Surface switch
            {
                SpecialSurface.TowerInterior => p.X > 13.5f && p.X < 30.5f && p.Z > 14 && p.Z < 32,
                SpecialSurface.TowerRoof => p.X > 13 && p.X < 31 && p.Z > 11 && p.Z < 33,
                SpecialSurface.TowerLanding when exitAlcove => p.X > 20 && p.X < 24 && p.Z > 11 && p.Z < 30,
                SpecialSurface.TowerLanding => d.Landing < TowerFloors * 2 && p.X > 14 && p.X < 30
                    && (d.Landing % 2 == 0 ? p.Z > 11 && p.Z < 14 : p.Z > 30 && p.Z < 33),
                SpecialSurface.HallRoof when d.LeanAgainstWall => p.X > -30 && p.X < 30 && p.Z > -36 && p.Z < -8,
                SpecialSurface.HallRoof => p.X > -27 && p.X < 18 && p.Z > -33 && p.Z < -11,
                SpecialSurface.CornerPlatform => Math.Abs(Math.Abs(p.X) - 35) < 2.5f - half.X
                    && Math.Abs(Math.Abs(p.Z) - 35) < 2.5f - half.Y,
                _ => p.X is > -32 and < 32 && p.Z is > -32 and < 32
                    && !(p.X > 11 && p.Z > 9) && !(p.X < 31 && p.X > -31 && p.Z < -7)
            };
            bool Fits(Vector3 p, float facing)
            {
                if (!WithinSurface(p)) return Reject("surface bounds");
                Vector3 actual = Transform(p);
                // Wall-backed seats use the pose's actual body/foot footprint
                // below; a standing bounding box would reject the backrest.
                if (!d.LeanAgainstWall && !PlacementSupport.HasFlatSupport(Collision, actual, half)) return Reject("support");
                if (!d.LeanAgainstWall && Collision.SurfaceOverlapsBox(actual + new Vector3(-half.X, .04f, -half.Y),
                        actual + new Vector3(half.X, d.Kind == SpecialKind.Exit ? 2.8f : 2.25f, half.Y))) return Reject("headroom");
                if (occupied.Any(o => Math.Abs(o.Foot.Y - p.Y) < 2.5f
                    && Vector2.Distance(new(o.Foot.X, o.Foot.Z), new(p.X, p.Z)) < o.Radius + half.Length() + .65f)) return Reject("other additions");
                if (People.Any(person => Math.Abs(person.Anchor.Y - actual.Y) < 2.5f
                    && Vector3.Distance(person.Anchor, actual) < 2.5f + Vector3.Distance(person.Anchor, person.Patrol))) return Reject("resident");
                if (d.MinResidentDistance > 0 && People.Any(person => Math.Abs(person.Anchor.Y - actual.Y) < 2.5f
                    && Vector2.Distance(new(person.Anchor.X, person.Anchor.Z), new(actual.X, actual.Z))
                        < d.MinResidentDistance + Vector3.Distance(person.Anchor, person.Patrol))) return Reject("resident spacing");
                bool CanApproach(float angle)
                {
                    Vector3 approach = actual + Rotate(new(MathF.Sin(angle), 0, MathF.Cos(angle))) * 2.19f;
                    Vector3 eye = approach + Vector3.UnitY * Explorer.EyeHeight;
                    return PlacementSupport.HasFlatSupport(Collision, approach, new(.35f)) && !Collision.Blocked(eye)
                        && !People.Any(person => person.Blocks(eye))
                        && (!(resident && corner) || !Collision.Obstructs(eye, actual + Vector3.UnitY * 1.4f * scale));
                }
                // A person gazing out from an edge can be approached from behind.
                if (!CanApproach(facing) && !(resident && corner && CanApproach(facing + MathF.PI)))
                    return Reject("examination approach");
                if (Routes.Any(route => route.Points.Zip(route.Points.Skip(1)).Any(segment =>
                {
                    Vector3 a = segment.First, delta = segment.Second - a;
                    float t = delta.LengthSquared() < .0001f ? 0 : Math.Clamp(Vector3.Dot(actual - a, delta) / delta.LengthSquared(), 0, 1);
                    Vector3 nearest = a + delta * t;
                    return Math.Abs(nearest.Y - actual.Y) < 1.8f
                        && Vector2.Distance(new(nearest.X, nearest.Z), new(actual.X, actual.Z)) < half.Length() + .45f;
                }))) return Reject("walking route");
                return !resident || (d.Pose == ResidentPose.Sitting
                    ? Residents.SittingSupported(Collision, actual, scale, facing - Rotation * MathF.PI / 2)
                    : Residents.Supported(Collision, actual, actual, scale * .48f));
            }
            Vector3? selected = null;
            // Fixed nearest-first integer offsets; deterministic even across
            // worker scheduling, without consuming any generator random stream.
            int radius = (int)MathF.Ceiling(d.SearchRadius);
            var candidates = from z in Enumerable.Range(-radius, radius * 2 + 1)
                             from x in Enumerable.Range(-radius, radius * 2 + 1)
                             where x * x + z * z <= d.SearchRadius * d.SearchRadius
                             orderby x * x + z * z, z, x
                             select requested + new Vector3(x, 0, z);
            foreach (Vector3 p in candidates)
            {
                if (d.LeanAgainstWall)
                {
                    bool Allowed(Vector3 seat, float facing)
                    {
                        Vector3 local = ResidentLocal(seat);
                        return Vector3.Distance(local, requested) <= d.SearchRadius + .01f
                            && Fits(local, facing + Rotation * MathF.PI / 2);
                    }
                    if (!Residents.TrySeatByWall(Collision, Transform(p), scale, out var seat, out float facing, Allowed)) continue;
                    selected = ResidentLocal(seat); yaw = facing + Rotation * MathF.PI / 2; break;
                }
                if (Fits(p, yaw)) { selected = p; break; }
            }
            if (selected is not { } foot)
                throw new InvalidDataException($"Cannot place special object '{d.Id}' on island {IslandNumber}, seed {worldSeed} ({string.Join(", ", rejected.Select(r => $"{r.Key}: {r.Value}"))}); adjust position/searchRadius in {SpecialContent.FilePath}.");
            occupied.Add((foot, half.Length()));
            if (resident)
            {
                var person = new Resident { Id = -1 - People.Count, Profile = 0, SpecialId = d.Id,
                    SpecialSpeech = new Dialogue.Speech { Id = 0, Name = d.Name, Age = d.Age, Gender = d.Gender,
                        Pose = d.Pose, Theme = "special", Pages = d.Pages, Choice = d.Choice },
                    Anchor = Transform(foot), Patrol = Transform(foot), Pose = d.Pose, Huddled = d.Huddled, Age = d.Age,
                    Female = d.Gender == "female", Scale = scale, Phase = 0 };
                person.Initialize(yaw - Rotation * MathF.PI / 2); People.Add(person);
                continue;
            }
            Geometry geometry = SpecialGeometry(d, foot);
            geometry.Transform(Rotate, Vector3.Zero);
            if (d.Kind != SpecialKind.Item) geometry.AddCollisionTo(Collision);
            if (d.Kind == SpecialKind.Item)
            {
                Color baseColor = new(148, 151, 142, 255);
                pedestal.Box(foot + new Vector3(0, .09f, 0), new(.8f, .18f, .8f), baseColor);
                pedestal.Cylinder(foot + Vector3.UnitY * .18f, .18f, .18f, .55f, baseColor);
                pedestal.Box(foot + new Vector3(0, .79f, 0), new(.72f, .12f, .72f), baseColor);
                pedestal.RemoveCoplanarOverlaps();
                pedestal.Transform(Rotate, Vector3.Zero); pedestal.AddCollisionTo(Collision);
            }
            SpecialObjects.Add(new() { Definition = d, LocalFoot = Rotate(foot),
                LocalFacing = Rotate(new(MathF.Sin(yaw), 0, MathF.Cos(yaw))), Geometry = geometry, PedestalGeometry = pedestal });
        }
    }

    private static Geometry SpecialGeometry(SpecialDefinition d, Vector3 foot)
    {
        if (d.Kind == SpecialKind.Exit) return ExitRelic.Build(foot, d.Tint);
        var g = new Geometry();
        if (d.Kind == SpecialKind.Item)
        {
            Color glass = new(205, 228, 228, 255), highlight = new(239, 248, 240, 255);
            Color liquid = d.Tint;
            // Visible liquid body, clear rim/shoulder and a narrow corked neck.
            g.Cylinder(foot + Vector3.UnitY * .85f, .2f, .27f, .12f, glass, 20);
            g.Cylinder(foot + Vector3.UnitY * .97f, .26f, .26f, .36f, liquid, 20);
            g.Ring(foot + Vector3.UnitY * 1.33f, .24f, .28f, .035f, glass, 20);
            g.Cylinder(foot + Vector3.UnitY * 1.365f, .28f, .11f, .16f, glass, 20);
            g.Ring(foot + Vector3.UnitY * 1.525f, .075f, .11f, .18f, glass, 20);
            g.Cylinder(foot + Vector3.UnitY * 1.705f, .095f, .09f, .1f, new(178, 137, 87, 255), 16);
            g.Box(foot + new Vector3(-.12f, 1.14f, .237f), new(.035f, .21f, .018f), highlight);
        }
        else
        {
            g = InscriptionSurface.Build(foot, d.Tint);
        }
        float angle = d.YawDegrees * MathF.PI / 180;
        g.RemoveCoplanarOverlaps();
        g.Transform(p => foot + Vector3.Transform(p - foot, Matrix4x4.CreateRotationY(angle)), Vector3.Zero);
        return g;
    }
}
