namespace Maple.Host.Recognition;

public static class RecognitionTargetFilter
{
    public static bool IsPlausibleMonster(RecognitionTarget candidate, SelfObservation? self = null)
    {
        if (!HasPositiveBounds(candidate) || candidate.Confidence <= 0) return false;
        double aspect = candidate.Width / candidate.Height;
        if (candidate.Height < 24 || candidate.Width < 12 || candidate.Width > 240 || candidate.Height > 260)
            return false;
        if (aspect < 0.20 || aspect > 2.20) return false;
        return self is null || IntersectionOverUnion(candidate, new RecognitionTarget(
            self.X, self.Y, self.Width, self.Height, "self", self.Confidence)) < 0.15;
    }

    public static bool IsPlausibleDrop(RecognitionTarget candidate, SelfObservation? self = null)
    {
        if (!HasPositiveBounds(candidate) || candidate.Confidence <= 0) return false;
        double aspect = candidate.Width / candidate.Height;
        if (candidate.Width > 110 || candidate.Height > 90 || aspect < 0.20 || aspect > 3.50)
            return false;
        return self is null || IntersectionOverUnion(candidate, new RecognitionTarget(
            self.X, self.Y, self.Width, self.Height, "self", self.Confidence)) < 0.10;
    }

    private static bool HasPositiveBounds(RecognitionTarget candidate) =>
        double.IsFinite(candidate.X) && double.IsFinite(candidate.Y)
        && double.IsFinite(candidate.Width) && double.IsFinite(candidate.Height)
        && candidate.Width > 0 && candidate.Height > 0;

    internal static double IntersectionOverUnion(RecognitionTarget first, RecognitionTarget second)
    {
        double left = Math.Max(first.X, second.X);
        double top = Math.Max(first.Y, second.Y);
        double right = Math.Min(first.X + first.Width, second.X + second.Width);
        double bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        double union = first.Width * first.Height + second.Width * second.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}

public sealed class RecognitionTargetStabilizer(int requiredObservations = 2, long maxGapFrames = 2)
{
    private readonly Dictionary<int, Track> tracks = [];
    private int nextId;

    public IReadOnlyList<RecognitionTarget> Update(IReadOnlyList<RecognitionTarget> candidates, long frameSequence)
    {
        var matched = new HashSet<int>();
        foreach (RecognitionTarget candidate in candidates)
        {
            Track? best = tracks.Values
                .Where(track => !matched.Contains(track.Id) && frameSequence - track.LastFrame <= maxGapFrames)
                .OrderBy(track => DistanceSquared(track.Target, candidate))
                .FirstOrDefault();
            double maxDistance = Math.Max(28, Math.Max(candidate.Width, candidate.Height) * 1.25);
            if (best is null || DistanceSquared(best.Target, candidate) > maxDistance * maxDistance)
            {
                best = new Track(++nextId, candidate, frameSequence, 1);
                tracks.Add(best.Id, best);
            }
            else
            {
                best.Target = candidate with { Confidence = Math.Max(best.Target.Confidence, candidate.Confidence) };
                best.LastFrame = frameSequence;
                best.Observations++;
            }
            matched.Add(best.Id);
        }

        foreach (int id in tracks.Values
            .Where(track => frameSequence - track.LastFrame > maxGapFrames)
            .Select(track => track.Id).ToArray())
            tracks.Remove(id);

        return tracks.Values
            .Where(track => track.Observations >= requiredObservations && frameSequence - track.LastFrame <= maxGapFrames)
            .Select(track => track.Target).ToArray();
    }

    private static double DistanceSquared(RecognitionTarget first, RecognitionTarget second)
    {
        double firstX = first.X + first.Width / 2;
        double firstY = first.Y + first.Height / 2;
        double secondX = second.X + second.Width / 2;
        double secondY = second.Y + second.Height / 2;
        return Math.Pow(firstX - secondX, 2) + Math.Pow(firstY - secondY, 2);
    }

    private sealed class Track(int id, RecognitionTarget target, long lastFrame, int observations)
    {
        public int Id { get; } = id;
        public RecognitionTarget Target { get; set; } = target;
        public long LastFrame { get; set; } = lastFrame;
        public int Observations { get; set; } = observations;
    }
}
