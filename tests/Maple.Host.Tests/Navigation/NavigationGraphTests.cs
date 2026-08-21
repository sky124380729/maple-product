using Maple.Host.Navigation;

namespace Maple.Host.Tests.Navigation;

public sealed class NavigationGraphTests
{
    [Fact]
    public void Finds_ladder_route_across_both_branches()
    {
        NavigationGraph graph = new(SwampShape());

        NavigationRoute route = graph.FindRoute(3, 6, 95);

        Assert.True(route.Success);
        Assert.Equal([3, 2, 1, 0, 4, 5, 6], route.PlatformIds);
        Assert.Equal(6, route.Edges.Length);
    }

    [Fact]
    public void Rejects_disconnected_ladder_graph()
    {
        MapPackageSnapshot map = SwampShape() with
        {
            Ladders = [new MapLadder(0, 50, 60, 70, [0, 1])]
        };

        NavigationGraphException exception = Assert.Throws<NavigationGraphException>(() => new NavigationGraph(map));

        Assert.Equal("MAP_GRAPH_UNSUPPORTED", exception.Code);
    }

    internal static MapPackageSnapshot SwampShape()
    {
        MapPlatform[] platforms =
        [
            new(0, 0, 200, 70), new(1, 10, 90, 60), new(2, 10, 90, 50), new(3, 10, 90, 40),
            new(4, 110, 190, 60), new(5, 110, 190, 50), new(6, 110, 190, 40)
        ];
        MapLadder[] ladders =
        [
            new(0, 50, 60, 70, [0, 1]), new(1, 50, 50, 60, [1, 2]), new(2, 50, 40, 50, [2, 3]),
            new(3, 150, 60, 70, [0, 4]), new(4, 150, 50, 60, [4, 5]), new(5, 150, 40, 50, [5, 6])
        ];
        return MapSignatureMatcherTests.Map() with { Platforms = [.. platforms], Ladders = [.. ladders] };
    }
}
