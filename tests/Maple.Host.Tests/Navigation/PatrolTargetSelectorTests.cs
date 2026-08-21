using Maple.Host.Navigation;

namespace Maple.Host.Tests.Navigation;

public sealed class PatrolTargetSelectorTests
{
    [Fact]
    public void Selects_least_recently_visited_non_current_platform()
    {
        NavigationGraph graph = new(NavigationGraphTests.SwampShape());
        PatrolTargetSelector selector = new(graph);
        selector.ConfirmArrival(1, 100);
        selector.ConfirmArrival(2, 200);
        selector.ConfirmArrival(3, 300);

        int target = selector.Select(3, currentX: 50);

        Assert.Equal(0, target);
    }

    [Fact]
    public void Uses_route_cost_then_platform_id_for_unvisited_ties()
    {
        NavigationGraph graph = new(NavigationGraphTests.SwampShape());
        PatrolTargetSelector selector = new(graph);

        int target = selector.Select(0, currentX: 50);

        Assert.Equal(1, target);
    }
}
