using Maple.Host.Navigation;
using Maple.Host.Preview;

namespace Maple.Host.Tests.Navigation;

public sealed class OpenSourceMapCatalogIntegrationTests
{
    [Fact]
    public async Task Loads_configured_open_source_package_directory()
    {
        string? directory = Environment.GetEnvironmentVariable("MAPLE_OPEN_SOURCE_MAP_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;

        MapCatalogResult result = await MapCatalog.ScanAsync(directory);

        Assert.Equal(42, result.Entries.Length);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Entries, entry => entry.WarningCode == "MAP_NAME_MISMATCH");
        Assert.Contains(result.Entries, entry => entry.Snapshot.Name == "沼泽地3" && entry.CanRun);
    }

    [Fact]
    public async Task Localizes_configured_real_map_frame()
    {
        string? packagePath = Environment.GetEnvironmentVariable("MAPLE_NAV_MAP_PACKAGE");
        string? framePath = Environment.GetEnvironmentVariable("MAPLE_NAV_FRAME_BGRA");
        if (string.IsNullOrWhiteSpace(packagePath) || string.IsNullOrWhiteSpace(framePath)) return;
        int width = int.Parse(Environment.GetEnvironmentVariable("MAPLE_NAV_FRAME_WIDTH")!);
        int height = int.Parse(Environment.GetEnvironmentVariable("MAPLE_NAV_FRAME_HEIGHT")!);
        await using FileStream package = File.OpenRead(packagePath);
        MapPackageSnapshot map = await MapPackageLoader.LoadAsync(package);
        byte[] pixels = await File.ReadAllBytesAsync(framePath);
        var frame = new CapturedFrame(width, height, width * 4, pixels, 100, 1);

        NavigationLocalization result = new MinimapLocalizer().Observe(frame, map, NavigationTraversal.None);

        Assert.True(result.MapMatched, $"fault={result.FaultCode}, confidence={result.MatchConfidence:0.000}");
        Assert.NotNull(result.Self);
        Assert.InRange(result.Self!.X, -5, map.MinimapRect!.Width + 5);
        Assert.InRange(result.Self.Y, -5, map.MinimapRect.Height + 5);
    }

    [Fact]
    public async Task Matches_configured_package_monster_in_real_map_frame()
    {
        string? framePath = Environment.GetEnvironmentVariable("MAPLE_NAV_FRAME_BGRA");
        string? templatePath = Environment.GetEnvironmentVariable("MAPLE_NAV_TEMPLATE_BGRA");
        if (string.IsNullOrWhiteSpace(framePath) || string.IsNullOrWhiteSpace(templatePath)) return;
        int width = int.Parse(Environment.GetEnvironmentVariable("MAPLE_NAV_FRAME_WIDTH")!);
        int height = int.Parse(Environment.GetEnvironmentVariable("MAPLE_NAV_FRAME_HEIGHT")!);
        int templateWidth = int.Parse(Environment.GetEnvironmentVariable("MAPLE_NAV_TEMPLATE_WIDTH")!);
        int templateHeight = int.Parse(Environment.GetEnvironmentVariable("MAPLE_NAV_TEMPLATE_HEIGHT")!);
        byte[] pixels = await File.ReadAllBytesAsync(framePath);
        byte[] templatePixels = await File.ReadAllBytesAsync(templatePath);
        var frame = new CapturedFrame(width, height, width * 4, pixels, 100, 3);
        var template = new BgraTemplate("swamp3", templateWidth, templateHeight, templatePixels);
        var projection = new MapViewportProjection();
        Assert.True(projection.TryProject(frame, new MapMinimapRect(5, 103, 223, 72), out ProjectedMapViewport viewport));

        IReadOnlyList<MonsterCandidate> matches = new MonsterTemplateMatcher().Match(
            frame,
            [template],
            0.55,
            viewport.MinimapRect);

        Assert.InRange(matches.Count, 3, 20);
        Assert.Contains(matches, match => match.X < 650 && match.Y < 420);
    }
}
