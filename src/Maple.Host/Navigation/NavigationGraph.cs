using System.Collections.Immutable;

namespace Maple.Host.Navigation;

public sealed class NavigationGraphException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public enum NavigationVerticalDirection { Up, Down }

public sealed record NavigationEdge(
    int FromPlatformId,
    int ToPlatformId,
    int LadderId,
    double ApproachX,
    NavigationVerticalDirection Direction,
    double Cost);

public sealed record NavigationRoute(
    bool Success,
    ImmutableArray<int> PlatformIds,
    ImmutableArray<NavigationEdge> Edges,
    double Cost)
{
    public static NavigationRoute Missing { get; } = new(false, [], [], double.PositiveInfinity);
}

public sealed class NavigationGraph
{
    private readonly IReadOnlyDictionary<int, MapPlatform> platforms;
    private readonly IReadOnlyDictionary<int, ImmutableArray<NavigationEdge>> adjacency;

    public NavigationGraph(MapPackageSnapshot map)
    {
        platforms = map.Platforms.ToDictionary(platform => platform.Id);
        if (platforms.Count == 0) throw Unsupported();
        Dictionary<int, List<NavigationEdge>> edges = platforms.Keys.ToDictionary(id => id, _ => new List<NavigationEdge>());
        foreach (MapLadder ladder in map.Ladders)
        {
            if (ladder.PlatformIds.Length != 2) throw Unsupported();
            int first = ladder.PlatformIds[0];
            int second = ladder.PlatformIds[1];
            if (!platforms.TryGetValue(first, out MapPlatform? a)
                || !platforms.TryGetValue(second, out MapPlatform? b)
                || !Approachable(a, ladder.X)
                || !Approachable(b, ladder.X))
                throw Unsupported();
            AddEdge(edges, ladder, a, b);
            AddEdge(edges, ladder, b, a);
        }
        adjacency = edges.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(edge => edge.ToPlatformId).ThenBy(edge => edge.LadderId).ToImmutableArray());
        if (Reachable(platforms.Keys.First()).Count != platforms.Count) throw Unsupported();
    }

    public IReadOnlyCollection<int> PlatformIds => platforms.Keys.ToArray();

    public NavigationRoute FindRoute(int fromPlatformId, int toPlatformId, double currentX)
    {
        if (!platforms.ContainsKey(fromPlatformId) || !platforms.ContainsKey(toPlatformId)) return NavigationRoute.Missing;
        if (fromPlatformId == toPlatformId) return new NavigationRoute(true, [fromPlatformId], [], 0);

        Dictionary<int, double> distance = platforms.Keys.ToDictionary(id => id, _ => double.PositiveInfinity);
        Dictionary<int, NavigationEdge> previous = [];
        PriorityQueue<int, (double Cost, int Platform)> queue = new();
        distance[fromPlatformId] = 0;
        queue.Enqueue(fromPlatformId, (0, fromPlatformId));
        while (queue.TryDequeue(out int current, out (double Cost, int Platform) priority))
        {
            if (priority.Cost > distance[current]) continue;
            if (current == toPlatformId) break;
            foreach (NavigationEdge edge in adjacency[current])
            {
                double approach = current == fromPlatformId
                    ? Math.Abs(currentX - edge.ApproachX)
                    : Math.Abs(previous[current].ApproachX - edge.ApproachX);
                double candidate = distance[current] + edge.Cost + approach;
                if (candidate >= distance[edge.ToPlatformId]) continue;
                distance[edge.ToPlatformId] = candidate;
                previous[edge.ToPlatformId] = edge;
                queue.Enqueue(edge.ToPlatformId, (candidate, edge.ToPlatformId));
            }
        }
        if (!previous.ContainsKey(toPlatformId)) return NavigationRoute.Missing;

        List<NavigationEdge> routeEdges = [];
        int cursor = toPlatformId;
        while (cursor != fromPlatformId)
        {
            NavigationEdge edge = previous[cursor];
            routeEdges.Add(edge);
            cursor = edge.FromPlatformId;
        }
        routeEdges.Reverse();
        return new NavigationRoute(
            true,
            [fromPlatformId, .. routeEdges.Select(edge => edge.ToPlatformId)],
            [.. routeEdges],
            distance[toPlatformId]);
    }

    private HashSet<int> Reachable(int start)
    {
        HashSet<int> visited = [start];
        Queue<int> pending = new([start]);
        while (pending.TryDequeue(out int current))
        foreach (NavigationEdge edge in adjacency[current])
            if (visited.Add(edge.ToPlatformId)) pending.Enqueue(edge.ToPlatformId);
        return visited;
    }

    private static void AddEdge(
        Dictionary<int, List<NavigationEdge>> edges,
        MapLadder ladder,
        MapPlatform from,
        MapPlatform to)
    {
        edges[from.Id].Add(new NavigationEdge(
            from.Id,
            to.Id,
            ladder.Id,
            ladder.X,
            to.Y < from.Y ? NavigationVerticalDirection.Up : NavigationVerticalDirection.Down,
            Math.Abs(to.Y - from.Y)));
    }

    private static bool Approachable(MapPlatform platform, double x) => x >= platform.XMin - 3 && x <= platform.XMax + 3;
    private static NavigationGraphException Unsupported() => new("MAP_GRAPH_UNSUPPORTED");
}
