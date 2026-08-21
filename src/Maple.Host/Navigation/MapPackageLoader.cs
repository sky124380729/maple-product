using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Maple.Host.Navigation;

public sealed class MapPackageLoadException : Exception
{
    public MapPackageLoadException(string code, string? detail = null, Exception? inner = null)
        : base(detail is null ? code : $"{code}:{detail}", inner) => Code = code;

    public string Code { get; }
}

public sealed record MapPackageThresholds(
    double Match,
    double MonsterColorCorrelation,
    int AttackRangePixels,
    int AttackRangeHeightPixels);

public sealed record MapPlatform(int Id, double XMin, double XMax, double Y);

public sealed record MapLadder(int Id, double X, double YMin, double YMax, ImmutableArray<int> PlatformIds);

public sealed record MapPlatformLink(
    int Id,
    int FromPlatform,
    double FromX,
    int ToPlatform,
    double ToX);

public sealed record MapStationPoint(int Id, double X, double Y, int? PlatformId);

public sealed record MapPackageTemplate(string Path, long SizeBytes, string Sha256);

public sealed record MapPackageSnapshot(
    string Name,
    MapPackageThresholds Thresholds,
    ImmutableArray<MapPlatform> Platforms,
    ImmutableArray<MapLadder> Ladders,
    ImmutableArray<MapPlatformLink> PlatformLinks,
    ImmutableArray<MapPlatformLink> JumpLinks,
    ImmutableArray<MapPlatformLink> DropLinks,
    ImmutableArray<MapPlatformLink> PortalLinks,
    ImmutableArray<MapPlatformLink> TeleportLinks,
    ImmutableArray<MapStationPoint> StationPoints,
    ImmutableArray<MapPackageTemplate> MonsterTemplates);

public static class MapPackageLoader
{
    private const string ExpectedFormat = "madudu_map_package";
    private const int ExpectedVersion = 1;
    private const int MaxEntries = 512;
    private const long MaxEntryBytes = 16 * 1024 * 1024;
    private const long MaxExpandedBytes = 64 * 1024 * 1024;
    private const long MaxJsonBytes = 2 * 1024 * 1024;

    public static async Task<MapPackageSnapshot> LoadAsync(Stream package, CancellationToken cancellationToken = default)
    {
        if (package is null || !package.CanRead)
            throw new MapPackageLoadException("MAP_PACKAGE_INVALID:STREAM");

        try
        {
            using ZipArchive archive = new(package, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxEntries)
                throw Invalid("ENTRY_COUNT");

            long expandedBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ValidatePath(entry.FullName);
                if (entry.Length < 0 || entry.Length > MaxEntryBytes)
                    throw Invalid("ENTRY_SIZE");
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MaxExpandedBytes)
                    throw Invalid("TOTAL_SIZE");
            }

            ZipArchiveEntry manifestEntry = FindEntry(archive, "manifest.json")
                ?? throw Invalid("MANIFEST_MISSING");
            ZipArchiveEntry mapEntry = FindEntry(archive, "map.json")
                ?? throw Invalid("MAP_MISSING");
            using JsonDocument manifest = await ReadJsonAsync(manifestEntry, cancellationToken);
            using JsonDocument map = await ReadJsonAsync(mapEntry, cancellationToken);
            ValidateManifest(manifest.RootElement);
            if (map.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalid("JSON_ROOT");

            MapPackageSnapshot snapshot = ParseSnapshot(manifest.RootElement, map.RootElement, archive);
            return snapshot;
        }
        catch (MapPackageLoadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new MapPackageLoadException("MAP_PACKAGE_INVALID:JSON", inner: exception);
        }
        catch (InvalidDataException exception)
        {
            throw new MapPackageLoadException("MAP_PACKAGE_INVALID:ZIP", inner: exception);
        }
        catch (OverflowException exception)
        {
            throw new MapPackageLoadException("MAP_PACKAGE_INVALID:SIZE", inner: exception);
        }
    }

    private static MapPackageSnapshot ParseSnapshot(JsonElement manifest, JsonElement map, ZipArchive archive)
    {
        string name = StringValue(manifest, "map_name") ?? StringValue(map, "name") ?? string.Empty;
        if (name.Length > 256) throw Invalid("NAME");

        ImmutableArray<MapPlatform> platforms = ParsePlatforms(map);
        HashSet<int> platformIds = platforms.Select(item => item.Id).ToHashSet();
        ImmutableArray<MapLadder> ladders = ParseLadders(map, platformIds);
        ImmutableArray<MapPlatformLink> platformLinks = ParseLinks(map, "platform_links", platformIds);
        ImmutableArray<MapPlatformLink> jumpLinks = ParseLinks(map, "jump_links", platformIds);
        ImmutableArray<MapPlatformLink> dropLinks = ParseLinks(map, "drop_links", platformIds);
        ImmutableArray<MapPlatformLink> portalLinks = ParseLinks(map, "portal_links", platformIds);
        ImmutableArray<MapPlatformLink> teleportLinks = ParseLinks(map, "teleport_links", platformIds);
        ImmutableArray<MapStationPoint> stationPoints = ParseStationPoints(map, platformIds);
        ImmutableArray<MapPackageTemplate> templates = archive.Entries
            .Where(entry => entry.FullName.StartsWith("mob_templates/", StringComparison.OrdinalIgnoreCase)
                && !entry.FullName.EndsWith('/'))
            .Select(ReadTemplate)
            .ToImmutableArray();

        return new MapPackageSnapshot(
            name,
            ParseThresholds(manifest, map),
            platforms,
            ladders,
            platformLinks,
            jumpLinks,
            dropLinks,
            portalLinks,
            teleportLinks,
            stationPoints,
            templates);
    }

    private static MapPackageThresholds ParseThresholds(JsonElement manifest, JsonElement map)
    {
        double match = Number(manifest, "match_threshold") ?? Number(map, "match_threshold") ?? 0.15;
        double color = Number(manifest, "monster_color_corr_threshold") ?? Number(map, "monster_color_corr_threshold") ?? 0.5;
        int range = Integer(manifest, "attack_range_pixels") ?? Integer(map, "attack_range_pixels") ?? 140;
        int height = Integer(manifest, "attack_range_height_pixels") ?? Integer(map, "attack_range_height_pixels") ?? 70;
        if (!double.IsFinite(match) || match is < 0 or > 1) throw Invalid("THRESHOLD");
        if (!double.IsFinite(color) || color is < 0 or > 1) throw Invalid("THRESHOLD");
        if (range <= 0 || height <= 0) throw Invalid("THRESHOLD");
        return new MapPackageThresholds(match, color, range, height);
    }

    private static ImmutableArray<MapPlatform> ParsePlatforms(JsonElement map)
    {
        ImmutableArray<MapPlatform>.Builder result = ImmutableArray.CreateBuilder<MapPlatform>();
        HashSet<int> ids = [];
        foreach (JsonElement element in Array(map, "platforms"))
        {
            if (element.ValueKind != JsonValueKind.Object) throw Invalid("PLATFORM_TYPE");
            int id = RequiredInteger(element, "id");
            if (!ids.Add(id)) throw Invalid("DUPLICATE_PLATFORM_ID");
            double[] range = RequiredNumberArray(element, "x_range", 2);
            double y = RequiredNumber(element, "y");
            if (range[0] > range[1]) throw Invalid("PLATFORM_RANGE");
            result.Add(new MapPlatform(id, range[0], range[1], y));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<MapLadder> ParseLadders(JsonElement map, HashSet<int> platformIds)
    {
        ImmutableArray<MapLadder>.Builder result = ImmutableArray.CreateBuilder<MapLadder>();
        HashSet<int> ids = [];
        foreach (JsonElement element in Array(map, "ladders"))
        {
            if (element.ValueKind != JsonValueKind.Object) throw Invalid("LADDER_TYPE");
            int id = RequiredInteger(element, "id");
            if (!ids.Add(id)) throw Invalid("DUPLICATE_LADDER_ID");
            double[] range = RequiredNumberArray(element, "y_range", 2);
            ImmutableArray<int> linkedPlatforms = RequiredIntegerArray(element, "platform_ids");
            if (range[0] > range[1] || linkedPlatforms.Any(item => !platformIds.Contains(item)))
                throw Invalid("LADDER_REFERENCE");
            result.Add(new MapLadder(id, RequiredNumber(element, "x"), range[0], range[1], linkedPlatforms));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<MapPlatformLink> ParseLinks(JsonElement map, string property, HashSet<int> platformIds)
    {
        ImmutableArray<MapPlatformLink>.Builder result = ImmutableArray.CreateBuilder<MapPlatformLink>();
        HashSet<int> ids = [];
        foreach (JsonElement element in Array(map, property))
        {
            if (element.ValueKind != JsonValueKind.Object) throw Invalid("LINK_TYPE");
            int id = RequiredInteger(element, "id");
            if (!ids.Add(id)) throw Invalid($"DUPLICATE_{property.ToUpperInvariant()}_ID");
            int from = RequiredInteger(element, "from_platform");
            int to = RequiredInteger(element, "to_platform");
            if (!platformIds.Contains(from) || !platformIds.Contains(to)) throw Invalid("LINK_REFERENCE");
            result.Add(new MapPlatformLink(id, from, RequiredNumber(element, "from_x"), to, RequiredNumber(element, "to_x")));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<MapStationPoint> ParseStationPoints(JsonElement map, HashSet<int> platformIds)
    {
        ImmutableArray<MapStationPoint>.Builder result = ImmutableArray.CreateBuilder<MapStationPoint>();
        HashSet<int> ids = [];
        foreach (JsonElement element in Array(map, "station_points"))
        {
            if (element.ValueKind != JsonValueKind.Object) throw Invalid("STATION_TYPE");
            int id = Integer(element, "id") ?? result.Count;
            if (!ids.Add(id)) throw Invalid("DUPLICATE_STATION_POINT_ID");
            int? platformId = Integer(element, "platform_id");
            if (platformId is not null && !platformIds.Contains(platformId.Value)) throw Invalid("STATION_REFERENCE");
            result.Add(new MapStationPoint(id, RequiredNumber(element, "x"), RequiredNumber(element, "y"), platformId));
        }
        return result.ToImmutable();
    }

    private static MapPackageTemplate ReadTemplate(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        return new MapPackageTemplate(entry.FullName, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static async Task<JsonDocument> ReadJsonAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length > MaxJsonBytes) throw Invalid("JSON_SIZE");
        using Stream stream = entry.Open();
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxJsonBytes) throw Invalid("JSON_SIZE");
        string json = Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
        return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static void ValidateManifest(JsonElement manifest)
    {
        if (manifest.ValueKind != JsonValueKind.Object) throw Invalid("JSON_ROOT");
        if (!string.Equals(StringValue(manifest, "format"), ExpectedFormat, StringComparison.Ordinal)) throw Invalid("FORMAT");
        if (Integer(manifest, "version") != ExpectedVersion) throw Invalid("VERSION");
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name) =>
        archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase));

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
            throw Invalid("PATH");
        string[] parts = path.Split('/');
        if (parts.Any(part => part is "" or "." or "..")) throw Invalid("PATH_TRAVERSAL");
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out JsonElement value)) return [];
        if (value.ValueKind != JsonValueKind.Array) throw Invalid($"{property.ToUpperInvariant()}_TYPE");
        return value.EnumerateArray();
    }

    private static double[] RequiredNumberArray(JsonElement parent, string property, int length)
    {
        if (!parent.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw Invalid($"{property.ToUpperInvariant()}_TYPE");
        double[] values = value.EnumerateArray().Select(item => Number(item) ?? throw Invalid("NUMBER")).ToArray();
        if (values.Length != length || values.Any(item => !double.IsFinite(item))) throw Invalid($"{property.ToUpperInvariant()}_RANGE");
        return values;
    }

    private static ImmutableArray<int> RequiredIntegerArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw Invalid($"{property.ToUpperInvariant()}_TYPE");
        return value.EnumerateArray().Select(item => Integer(item) ?? throw Invalid("INTEGER")).ToImmutableArray();
    }

    private static double RequiredNumber(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement value)
            ? Number(value) is double number && double.IsFinite(number) ? number : throw Invalid("NUMBER")
            : throw Invalid($"{property.ToUpperInvariant()}_MISSING");

    private static int RequiredInteger(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement value) ? Integer(value) ?? throw Invalid("INTEGER") : throw Invalid($"{property.ToUpperInvariant()}_MISSING");

    private static string? StringValue(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement value) ? Number(value) : null;

    private static double? Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result) ? result : null;

    private static int? Integer(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out JsonElement value) ? Integer(value) : null;

    private static int? Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : null;

    private static MapPackageLoadException Invalid(string code) => new($"MAP_PACKAGE_INVALID:{code}");
}
