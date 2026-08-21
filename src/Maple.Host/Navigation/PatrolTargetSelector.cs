namespace Maple.Host.Navigation;

public sealed class PatrolTargetSelector(NavigationGraph graph)
{
    private readonly Dictionary<int, long> arrivals = [];

    public void ConfirmArrival(int platformId, long monoMs) => arrivals[platformId] = monoMs;

    public int Select(int currentPlatformId, double currentX)
    {
        return graph.PlatformIds
            .Where(id => id != currentPlatformId)
            .Select(id => new
            {
                Id = id,
                Arrived = arrivals.TryGetValue(id, out long value) ? value : long.MinValue,
                Route = graph.FindRoute(currentPlatformId, id, currentX)
            })
            .Where(item => item.Route.Success)
            .OrderBy(item => item.Arrived)
            .ThenBy(item => item.Route.Cost)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .FirstOrDefault(currentPlatformId);
    }
}
