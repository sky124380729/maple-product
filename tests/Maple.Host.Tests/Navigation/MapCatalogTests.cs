using System.IO.Compression;
using System.Text;
using Maple.Host.Navigation;

namespace Maple.Host.Tests.Navigation;

public sealed class MapCatalogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"maple-catalog-{Guid.NewGuid():N}");

    public MapCatalogTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Scans_only_mapzip_and_marks_name_mismatch()
    {
        CreatePackage("Swamp(30-45).mapzip", "Swamp");
        CreatePackage("Wrong.mapzip", "Actual");
        File.WriteAllText(Path.Combine(directory, "ignored.zip"), "not a package");

        MapCatalogResult result = await MapCatalog.ScanAsync(directory);

        Assert.Equal(2, result.Entries.Length);
        Assert.True(result.Entries.Single(entry => entry.FileName == "Swamp(30-45).mapzip").CanRun);
        MapCatalogEntry mismatch = result.Entries.Single(entry => entry.FileName == "Wrong.mapzip");
        Assert.False(mismatch.CanRun);
        Assert.Equal("MAP_NAME_MISMATCH", mismatch.WarningCode);
    }

    [Fact]
    public async Task Disables_duplicate_map_names()
    {
        CreatePackage("Same(1).mapzip", "Same");
        CreatePackage("Same(2).mapzip", "Same");

        MapCatalogResult result = await MapCatalog.ScanAsync(directory);

        Assert.All(result.Entries, entry =>
        {
            Assert.False(entry.CanRun);
            Assert.Equal("MAP_NAME_DUPLICATE", entry.WarningCode);
        });
    }

    [Fact]
    public async Task Hash_changes_when_package_content_changes()
    {
        string path = CreatePackage("Swamp.mapzip", "Swamp");
        string first = Assert.Single((await MapCatalog.ScanAsync(directory)).Entries).Sha256;

        File.Delete(path);
        CreatePackage("Swamp.mapzip", "Swamp", marker: "changed");
        string second = Assert.Single((await MapCatalog.ScanAsync(directory)).Entries).Sha256;

        Assert.NotEqual(first, second);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string CreatePackage(string fileName, string mapName, string marker = "initial")
    {
        string path = Path.Combine(directory, fileName);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", $"{{\"format\":\"madudu_map_package\",\"version\":1,\"map_name\":\"{mapName}\",\"minimap_rect\":[0,0,10,10],\"marker\":\"{marker}\"}}");
        WriteEntry(archive, "map.json", "{\"platforms\":[]}");
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
