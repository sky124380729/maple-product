using Maple.Host.Navigation;

namespace Maple.Host.Tests.Navigation;

public sealed class MinimapLocalizerTests
{
    [Fact]
    public void Localizes_marker_to_unique_platform()
    {
        MapPackageSnapshot map = MapSignatureMatcherTests.Map();

        NavigationLocalization result = new MinimapLocalizer().Observe(
            MapSignatureMatcherTests.Frame(120, 80, 4, 50, true),
            map,
            NavigationTraversal.None);

        Assert.Equal(new MapPoint(50, 20), result.Self);
        Assert.Equal(0, result.PlatformId);
        Assert.True(result.MapMatched);
    }

    [Fact]
    public void Rejects_ambiguous_platform_assignment()
    {
        MapPackageSnapshot map = MapSignatureMatcherTests.Map() with
        {
            Platforms =
            [
                new MapPlatform(0, 10, 90, 20),
                new MapPlatform(1, 20, 80, 22)
            ]
        };

        NavigationLocalization result = new MinimapLocalizer().Observe(
            MapSignatureMatcherTests.Frame(120, 80, 4, 50, true),
            map,
            NavigationTraversal.None);

        Assert.Null(result.PlatformId);
        Assert.Equal("SELF_NOT_LOCALIZED", result.FaultCode);
    }

    [Fact]
    public void Allows_null_platform_while_traversing_connector()
    {
        NavigationLocalization result = new MinimapLocalizer().Observe(
            MapSignatureMatcherTests.Frame(120, 80, 4, 50, true, markerY: 40),
            MapSignatureMatcherTests.Map(),
            NavigationTraversal.Connector);

        Assert.Null(result.PlatformId);
        Assert.Null(result.FaultCode);
    }
}
