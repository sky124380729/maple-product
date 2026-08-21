namespace Maple.Host.Navigation;

public sealed record MapPoint(double X, double Y);

public enum NavigationTraversal
{
    None,
    Connector
}

public sealed record NavigationLocalization(
    long FrameSequence,
    long CapturedAtMonoMs,
    bool MapMatched,
    double MatchConfidence,
    MapPoint? Self,
    int? PlatformId,
    string? FaultCode);

public sealed class NavigationLocalizationGate
{
    private long lastSequence = -1;
    private long lastMatchAt = -1;
    private int consecutiveMatches;
    private int consecutiveMismatches;
    private bool armed;

    public NavigationLocalization Update(NavigationLocalization observation)
    {
        if (observation.FrameSequence <= lastSequence) return observation with
        {
            MapMatched = armed,
            FaultCode = armed ? null : observation.FaultCode
        };
        lastSequence = observation.FrameSequence;

        if (observation.MapMatched && observation.Self is not null)
        {
            consecutiveMatches++;
            consecutiveMismatches = 0;
            lastMatchAt = observation.CapturedAtMonoMs;
            if (consecutiveMatches >= 5) armed = true;
        }
        else
        {
            consecutiveMatches = 0;
            consecutiveMismatches++;
        }

        if (armed && lastMatchAt >= 0 && observation.CapturedAtMonoMs - lastMatchAt > 500)
        {
            armed = false;
            return observation with { MapMatched = false, FaultCode = "OBSERVATION_STALE" };
        }
        if (armed && consecutiveMismatches >= 3)
        {
            armed = false;
            return observation with { MapMatched = false, FaultCode = "MAP_MISMATCH" };
        }
        return observation with { MapMatched = armed, FaultCode = armed ? null : observation.FaultCode };
    }
}
