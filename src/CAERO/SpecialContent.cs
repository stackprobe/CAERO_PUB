using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raylib_cs;

namespace Caero;

internal enum SpecialKind { Inscription, Resident, Item, Exit }
internal enum SpecialSurface { Ground, HallRoof, TowerInterior, CornerPlatform, TowerRoof, TowerLanding }
internal enum ItemEffect { None, Dash, Jump, Flight }

internal sealed record SpecialDefinition
{
    public required string Id { get; init; }
    public required string Island { get; init; }
    public required SpecialKind Kind { get; init; }
    public required string Name { get; init; }
    public required SpecialSurface Surface { get; init; }
    public int Landing { get; init; }
    public required float[] Position { get; init; }
    public float YawDegrees { get; init; }
    public float SearchRadius { get; init; }
    public required string Color { get; init; }
    public ResidentPose Pose { get; init; } = ResidentPose.Standing;
    public bool LeanAgainstWall { get; init; }
    public bool Huddled { get; init; }
    public float MinResidentDistance { get; init; }
    public ResidentAge Age { get; init; } = ResidentAge.Adult;
    public string Gender { get; init; } = "male";
    public ItemEffect Effect { get; init; }
    public required string[] Pages { get; init; }
    public DialogueChoice? Choice { get; init; }
    internal Color Tint => new(Convert.ToInt32(Color[..2], 16), Convert.ToInt32(Color[2..4], 16), Convert.ToInt32(Color[4..], 16), 255);
}

internal sealed record SpecialCatalogue
{
    public int Version { get; init; }
    public float DashBonusPerItem { get; init; }
    public float JumpBonusPerItem { get; init; }
    public required SpecialDefinition[] Objects { get; init; }
}

internal static class SpecialContent
{
    internal static string FilePath => Path.Combine(AppContext.BaseDirectory, "Content", "special-islands.ja.json");
    internal static readonly SpecialCatalogue Data = Load(FilePath);
    private static readonly Dictionary<BigInteger, SpecialDefinition[]> Islands = Data.Objects
        .GroupBy(d => BigInteger.Parse(d.Island, CultureInfo.InvariantCulture)).ToDictionary(g => g.Key, g => g.ToArray());
    internal static readonly IReadOnlyDictionary<string, SpecialDefinition> ById = Data.Objects.ToDictionary(d => d.Id);
    internal static IEnumerable<SpecialDefinition> ForIsland(BigInteger number)
        => Islands.TryGetValue(number, out var entries) ? entries : [];
    internal static string FontCharacters => string.Concat(Data.Objects.Select(d => d.Name + string.Concat(d.Pages)
        + (d.Choice is { } choice ? string.Concat(choice.AllPages) : "")))
        + "調べる取得するあなたは脱出した飛行能力が必要です碑文アイテム青い薬がある。赤い薬がある。緑の薬がある。飲んでみますか？";

    internal static SpecialCatalogue Load(string path)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            var data = JsonSerializer.Deserialize<SpecialCatalogue>(ContentAssets.ReadText(path), options)
                ?? throw new InvalidDataException("Empty catalogue");
            if (data.Version != 1 || data.Objects == null || !float.IsFinite(data.DashBonusPerItem)
                || data.DashBonusPerItem is < 0 or > 20 || !float.IsFinite(data.JumpBonusPerItem)
                || data.JumpBonusPerItem is < 0 or > .5f)
                throw new InvalidDataException("Invalid version or ability increments");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in data.Objects)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.Id) || !ids.Add(d.Id)
                    || !BigInteger.TryParse(d.Island, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1
                    || !Enum.IsDefined(d.Kind) || !Enum.IsDefined(d.Surface) || !Enum.IsDefined(d.Pose) || !Enum.IsDefined(d.Age)
                    || (d.Surface == SpecialSurface.TowerLanding ? d.Landing is < 1 or > 5 : d.Landing != 0)
                    || !Enum.IsDefined(d.Effect) || d.Gender is not ("male" or "female")
                    || string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 24 || !DialogueChoice.ValidPages(d.Pages)
                    || (d.Choice != null && (!d.Choice.IsValid
                        || d.Pages.Concat(d.Choice.AllPages).Sum(p => (long)p.Length) > 20000))
                    || d.Position is not { Length: 3 } || d.Position.Any(v => !float.IsFinite(v))
                    || Math.Abs(d.Position[0]) > 85 || Math.Abs(d.Position[2]) > 85 || Math.Abs(d.Position[1]) > .01f
                    || !float.IsFinite(d.YawDegrees) || !float.IsFinite(d.SearchRadius) || d.SearchRadius is < 0 or > 12
                    || d.Color == null || d.Color.Length != 6 || !uint.TryParse(d.Color, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
                    || (d.Kind == SpecialKind.Item) != (d.Effect != ItemEffect.None)
                    || (d.Kind == SpecialKind.Resident && d.Pose == ResidentPose.Walking)
                    || (d.Huddled && (d.Kind != SpecialKind.Resident || d.Pose != ResidentPose.Sitting))
                    || !float.IsFinite(d.MinResidentDistance) || d.MinResidentDistance is < 0 or > 20
                    || (d.MinResidentDistance > 0 && d.Kind != SpecialKind.Resident)
                    || (d.LeanAgainstWall && (d.Kind != SpecialKind.Resident || d.Pose != ResidentPose.Sitting)))
                    throw new InvalidDataException($"Invalid entry '{d?.Id}': check IDs, island, placement, metadata, effect and pages");
            }
            return data;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        { throw new InvalidDataException($"Special island settings: {path}: {ex.Message}", ex); }
    }

}

internal sealed class SpecialObject : IDisposable
{
    internal required SpecialDefinition Definition { get; init; }
    internal required Vector3 LocalFoot { get; init; }
    internal required Vector3 LocalFacing { get; init; }
    internal required Geometry Geometry { get; init; }
    internal Geometry PedestalGeometry { get; init; } = new();
    internal SceneChunk? Chunk { get; private set; }
    internal SceneChunk? PedestalChunk { get; private set; }
    internal Vector3 Offset { get; private set; }
    internal Vector3 Foot => LocalFoot + Offset;
    internal Vector3 Point => Foot + Vector3.UnitY * 1.25f;
    // Target a point just outside the front surface, so its own collision never
    // makes an inscription impossible to examine.
    internal Vector3 ExaminePoint => Point + LocalFacing * (Definition.Kind == SpecialKind.Exit ? .7f : .19f);
    internal void SetOrigin(Vector3 offset)
    { Offset = offset; if (Chunk != null) Chunk.Offset = offset; if (PedestalChunk != null) PedestalChunk.Offset = offset; }
    internal void Upload()
    {
        SceneChunk UploadPart(Geometry geometry, bool smooth = false, bool inscription = false)
        {
            var part = new SceneChunk { Detailed = inscription
                    ? geometry.UploadMapped(p => InscriptionSurface.Coordinates(p, LocalFoot, LocalFacing)) : geometry.UploadTiled(),
                LocalMin = geometry.Vertices.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min),
                LocalMax = geometry.Vertices.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max),
                Offset = Offset, DetailedTriangles = geometry.VertexCount / 3, Surface = SurfaceSet.Architecture, SmoothMaterial = smooth };
            geometry.Clear(); return part;
        }
        Chunk = UploadPart(Geometry, Definition.Kind is SpecialKind.Item or SpecialKind.Exit, Definition.Kind == SpecialKind.Inscription);
        Chunk.ExitRelic = Definition.Kind == SpecialKind.Exit;
        Chunk.Inscription = Definition.Kind == SpecialKind.Inscription;
        if (PedestalGeometry.VertexCount > 0) PedestalChunk = UploadPart(PedestalGeometry);
    }
    public void Dispose() { Chunk?.Dispose(); PedestalChunk?.Dispose(); Chunk = PedestalChunk = null; Geometry.Clear(); PedestalGeometry.Clear(); }
}
