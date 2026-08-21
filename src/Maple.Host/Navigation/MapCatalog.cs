using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Maple.Host.Navigation;

public sealed record MapCatalogEntry(
    string PackagePath,
    string FileName,
    string Sha256,
    MapPackageSnapshot Snapshot,
    bool CanRun,
    string? WarningCode);

public sealed record MapCatalogResult(
    ImmutableArray<MapCatalogEntry> Entries,
    ImmutableArray<string> Errors);

public static partial class MapCatalog
{
    public static async Task<MapCatalogResult> ScanAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root))
            return new MapCatalogResult([], ["MAP_DIRECTORY_NOT_FOUND"]);
        if (IsReparsePoint(root))
            return new MapCatalogResult([], ["MAP_DIRECTORY_LINK_UNSUPPORTED"]);

        List<MapCatalogEntry> entries = [];
        List<string> errors = [];
        foreach (string path in Directory.EnumerateFiles(root, "*.mapzip", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(path))
            {
                errors.Add($"MAP_PACKAGE_LINK_UNSUPPORTED:{Path.GetFileName(path)}");
                continue;
            }

            try
            {
                await using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
                    .ToLowerInvariant();
                stream.Position = 0;
                MapPackageSnapshot snapshot = await MapPackageLoader.LoadAsync(stream, cancellationToken);
                string fileName = Path.GetFileName(path);
                bool nameMatches = string.Equals(
                    PackageLabelRegex().Replace(Path.GetFileNameWithoutExtension(path), string.Empty).Trim(),
                    snapshot.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase);
                string? warning = !nameMatches
                    ? "MAP_NAME_MISMATCH"
                    : snapshot.MinimapRect is null
                        ? "MAP_MINIMAP_RECT_MISSING"
                        : !snapshot.PlanningReady
                            ? "MAP_PLANNING_NOT_READY"
                            : null;
                entries.Add(new MapCatalogEntry(
                    Path.GetFullPath(path),
                    fileName,
                    hash,
                    snapshot,
                    warning is null,
                    warning));
            }
            catch (Exception exception) when (exception is MapPackageLoadException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}:{exception.Message}");
            }
        }

        foreach (IGrouping<string, MapCatalogEntry> duplicate in entries.GroupBy(
                     entry => entry.Snapshot.Name,
                     StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (MapCatalogEntry item in duplicate.ToArray())
            {
                int index = entries.IndexOf(item);
                entries[index] = item with { CanRun = false, WarningCode = "MAP_NAME_DUPLICATE" };
            }
        }

        return new MapCatalogResult(
            entries.OrderBy(entry => entry.Snapshot.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray(),
            errors.OrderBy(error => error, StringComparer.OrdinalIgnoreCase).ToImmutableArray());
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    [GeneratedRegex(@"\s*\([^)]*\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageLabelRegex();
}
