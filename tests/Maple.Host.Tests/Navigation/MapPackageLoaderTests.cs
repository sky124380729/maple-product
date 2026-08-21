using System.IO.Compression;
using System.Text;
using Maple.Host.Navigation;

namespace Maple.Host.Tests.Navigation;

public sealed class MapPackageLoaderTests
{
    [Fact]
    public async Task Loads_navigation_minimap_metadata()
    {
        await using MemoryStream package = CreatePackage(
            manifest: "{\"format\":\"madudu_map_package\",\"version\":1,\"map_name\":\"Swamp\",\"minimap_rect\":[5,103,223,72],\"minimap_rect_source\":\"manual\"}",
            map: "{\"platforms\":[]}");

        MapPackageSnapshot snapshot = await MapPackageLoader.LoadAsync(package);

        Assert.Equal(new MapMinimapRect(5, 103, 223, 72), snapshot.MinimapRect);
        Assert.Equal("manual", snapshot.MinimapRectSource);
    }

    [Theory]
    [InlineData("[-1,0,10,10]")]
    [InlineData("[0,0,0,10]")]
    [InlineData("[0,0,10,-1]")]
    public async Task Rejects_invalid_navigation_minimap_metadata(string rect)
    {
        await using MemoryStream package = CreatePackage(
            manifest: $"{{\"format\":\"madudu_map_package\",\"version\":1,\"minimap_rect\":{rect}}}",
            map: "{\"platforms\":[]}");

        MapPackageLoadException exception = await Assert.ThrowsAsync<MapPackageLoadException>(
            () => MapPackageLoader.LoadAsync(package));

        Assert.Equal("MAP_PACKAGE_INVALID:MINIMAP_RECT", exception.Code);
    }

    [Fact]
    public async Task Loads_map_graph_thresholds_and_template_metadata()
    {
        await using MemoryStream package = CreatePackage(
            manifest: "{\"format\":\"madudu_map_package\",\"version\":1,\"map_name\":\"Test Map\",\"match_threshold\":0.15,\"monster_color_corr_threshold\":0.5,\"attack_range_pixels\":140,\"attack_range_height_pixels\":70}",
            map: "{\"name\":\"Test Map\",\"platforms\":[{\"id\":1,\"x_range\":[10,90],\"y\":20}],\"ladders\":[{\"id\":2,\"x\":30,\"y_range\":[20,40],\"platform_ids\":[1]}],\"platform_links\":[],\"jump_links\":[],\"drop_links\":[],\"portal_links\":[],\"teleport_links\":[],\"station_points\":[]}",
            ("mob_templates/templates/slime.png", new byte[] { 1, 2, 3 }));

        MapPackageSnapshot snapshot = await MapPackageLoader.LoadAsync(package);

        Assert.Equal("Test Map", snapshot.Name);
        Assert.Equal(0.15, snapshot.Thresholds.Match, 3);
        Assert.Equal(140, snapshot.Thresholds.AttackRangePixels);
        Assert.Single(snapshot.Platforms);
        Assert.Equal(10, snapshot.Platforms[0].XMin);
        Assert.Equal(90, snapshot.Platforms[0].XMax);
        Assert.Single(snapshot.Ladders);
        Assert.Equal(1, snapshot.Ladders[0].PlatformIds[0]);
        Assert.Single(snapshot.MonsterTemplates);
        Assert.Equal("mob_templates/templates/slime.png", snapshot.MonsterTemplates[0].Path);
        Assert.Equal(3, snapshot.MonsterTemplates[0].SizeBytes);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.MonsterTemplates[0].Sha256));
        Assert.True(snapshot.PlanningReady);
        Assert.Empty(snapshot.QualityReasons);
    }

    [Fact]
    public async Task Preserves_recording_quality_metadata()
    {
        await using MemoryStream package = CreatePackage(
            manifest: "{\"format\":\"madudu_map_package\",\"version\":1,\"planning_ready\":false,\"quality_reasons\":[\"CONNECTIVITY_MISSING\"]}",
            map: "{\"platforms\":[]}");

        MapPackageSnapshot snapshot = await MapPackageLoader.LoadAsync(package);

        Assert.False(snapshot.PlanningReady);
        Assert.Equal(["CONNECTIVITY_MISSING"], snapshot.QualityReasons);
    }

    [Fact]
    public async Task Rejects_inconsistent_recording_quality_metadata()
    {
        await using MemoryStream package = CreatePackage(
            manifest: "{\"format\":\"madudu_map_package\",\"version\":1,\"planning_ready\":true,\"quality_reasons\":[\"CONNECTIVITY_MISSING\"]}",
            map: "{\"platforms\":[]}");

        MapPackageLoadException exception = await Assert.ThrowsAsync<MapPackageLoadException>(
            () => MapPackageLoader.LoadAsync(package));

        Assert.Equal("MAP_PACKAGE_INVALID:QUALITY_INCONSISTENT", exception.Code);
    }

    [Theory]
    [InlineData("manifest.json", "MAP_PACKAGE_INVALID:MANIFEST_MISSING")]
    [InlineData("map.json", "MAP_PACKAGE_INVALID:MAP_MISSING")]
    public async Task Rejects_missing_required_entry(string missing, string expectedCode)
    {
        await using MemoryStream package = CreatePackage(
            manifest: missing == "manifest.json" ? null : "{\"format\":\"madudu_map_package\",\"version\":1}",
            map: missing == "map.json" ? null : "{\"platforms\":[]}");

        MapPackageLoadException exception = await Assert.ThrowsAsync<MapPackageLoadException>(
            () => MapPackageLoader.LoadAsync(package));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task Rejects_duplicate_platform_ids_and_dangling_links()
    {
        await using MemoryStream package = CreatePackage(
            manifest: "{\"format\":\"madudu_map_package\",\"version\":1}",
            map: "{\"platforms\":[{\"id\":1,\"x_range\":[0,1],\"y\":1},{\"id\":1,\"x_range\":[2,3],\"y\":2}],\"platform_links\":[{\"id\":1,\"from_platform\":1,\"from_x\":1,\"to_platform\":9,\"to_x\":1}]}");

        MapPackageLoadException exception = await Assert.ThrowsAsync<MapPackageLoadException>(
            () => MapPackageLoader.LoadAsync(package));

        Assert.Equal("MAP_PACKAGE_INVALID:DUPLICATE_PLATFORM_ID", exception.Code);
    }

    [Fact]
    public async Task Rejects_path_traversal_entries_before_reading_them()
    {
        await using MemoryStream package = CreatePackage(
            manifest: "{\"format\":\"madudu_map_package\",\"version\":1}",
            map: "{\"platforms\":[]}",
            ("../outside.bin", new byte[] { 1 }));

        MapPackageLoadException exception = await Assert.ThrowsAsync<MapPackageLoadException>(
            () => MapPackageLoader.LoadAsync(package));

        Assert.Equal("MAP_PACKAGE_INVALID:PATH_TRAVERSAL", exception.Code);
    }

    [Fact]
    public async Task Rejects_invalid_format_and_out_of_range_geometry()
    {
        await using MemoryStream package = CreatePackage(
            manifest: "{\"format\":\"other\",\"version\":1}",
            map: "{\"platforms\":[{\"id\":1,\"x_range\":[4,3],\"y\":1}]}");

        MapPackageLoadException exception = await Assert.ThrowsAsync<MapPackageLoadException>(
            () => MapPackageLoader.LoadAsync(package));

        Assert.Equal("MAP_PACKAGE_INVALID:FORMAT", exception.Code);
    }

    private static MemoryStream CreatePackage(string? manifest, string? map, params (string Path, byte[] Content)[] extra)
    {
        MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (manifest is not null) WriteEntry(archive, "manifest.json", manifest);
            if (map is not null) WriteEntry(archive, "map.json", map);
            foreach ((string path, byte[] content) in extra)
            {
                ZipArchiveEntry entry = archive.CreateEntry(path);
                using Stream output = entry.Open();
                output.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
