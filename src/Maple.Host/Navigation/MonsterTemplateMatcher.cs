using Maple.Host.Preview;
using Maple.Host.Recognition;

namespace Maple.Host.Navigation;

public sealed record BgraTemplate(
    string Name,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels);

public sealed record MonsterCandidate(double X, double Y, double Width, double Height, double Confidence);

public sealed class MonsterTemplateMatcher
{
    public IReadOnlyList<MonsterCandidate> Match(
        CapturedFrame frame,
        IReadOnlyList<BgraTemplate> templates,
        double threshold,
        MapMinimapRect? minimapRect)
    {
        List<MonsterCandidate> found = [];
        foreach (BgraTemplate template in templates)
        {
            if (template.Width <= 0 || template.Height <= 0
                || template.Pixels.Length < template.Width * template.Height * 4
                || template.Width > frame.Width || template.Height > frame.Height)
                continue;
            int maxY = Math.Max(0, (int)(frame.Height * 0.86) - template.Height);
            for (int y = 0; y <= maxY; y += 2)
            for (int x = 0; x <= frame.Width - template.Width; x += 2)
            {
                if (OverlapsMinimap(x, y, template.Width, template.Height, minimapRect)) continue;
                double score = Score(frame, template, x, y);
                if (score < threshold) continue;
                MonsterCandidate candidate = new(x, y, template.Width, template.Height, score);
                if (found.Any(item => IoU(item, candidate) > 0.3)) continue;
                found.Add(candidate);
                if (found.Count >= 24) return found;
            }
        }
        return found.OrderByDescending(candidate => candidate.Confidence).ToArray();
    }

    private static double Score(CapturedFrame frame, BgraTemplate template, int x, int y)
    {
        ReadOnlySpan<byte> source = template.Pixels.Span;
        ReadOnlySpan<byte> target = frame.BgraPixels.Span;
        int sampled = 0;
        double score = 0;
        int step = Math.Max(1, template.Width * template.Height / 64);
        for (int pixel = 0; pixel < template.Width * template.Height; pixel += step)
        {
            int sourceOffset = pixel * 4;
            if (source[sourceOffset + 3] < 100) continue;
            int localX = pixel % template.Width;
            int localY = pixel / template.Width;
            int targetOffset = (y + localY) * frame.Stride + (x + localX) * 4;
            int distance = Math.Abs(source[sourceOffset] - target[targetOffset])
                + Math.Abs(source[sourceOffset + 1] - target[targetOffset + 1])
                + Math.Abs(source[sourceOffset + 2] - target[targetOffset + 2]);
            score += 1 - Math.Min(765, distance) / 765d;
            sampled++;
        }
        return sampled == 0 ? 0 : score / sampled;
    }

    private static bool OverlapsMinimap(int x, int y, int width, int height, MapMinimapRect? roi) =>
        roi is not null && x < roi.X + roi.Width && x + width > roi.X && y < roi.Y + roi.Height && y + height > roi.Y;

    internal static double IoU(MonsterCandidate a, MonsterCandidate b)
    {
        double left = Math.Max(a.X, b.X), top = Math.Max(a.Y, b.Y);
        double right = Math.Min(a.X + a.Width, b.X + b.Width), bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        double union = a.Width * a.Height + b.Width * b.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}

public sealed class MonsterTargetStabilizer
{
    private readonly List<Track> tracks = [];

    public IReadOnlyList<MonsterCandidate> Update(
        long frameSequence,
        IReadOnlyList<MonsterCandidate> templateMatches,
        IReadOnlyList<RecognitionTarget> recognitionMonsters,
        IReadOnlyList<RecognitionTarget> excludedActors)
    {
        foreach (MonsterCandidate candidate in templateMatches)
        {
            if (excludedActors.Any(actor => Overlap(candidate, actor) >= 0.15)) continue;
            Track? track = tracks.Where(item => frameSequence - item.LastFrame <= 2)
                .OrderBy(item => Distance(item.Candidate, candidate)).FirstOrDefault();
            if (track is null || Distance(track.Candidate, candidate) > Math.Max(12, candidate.Width))
            {
                tracks.Add(new Track(candidate, frameSequence, 1));
                continue;
            }
            if (track.LastFrame != frameSequence) track.Observations++;
            bool corroborated = recognitionMonsters.Any(actor => Overlap(candidate, actor) >= 0.15);
            track.Candidate = candidate with { Confidence = Math.Min(1, candidate.Confidence + (corroborated ? 0.05 : 0)) };
            track.LastFrame = frameSequence;
        }
        tracks.RemoveAll(track => frameSequence - track.LastFrame > 2);
        return tracks.Where(track => track.Observations >= 2 && track.LastFrame == frameSequence)
            .Select(track => track.Candidate).ToArray();
    }

    private static double Overlap(MonsterCandidate candidate, RecognitionTarget actor) =>
        MonsterTemplateMatcher.IoU(candidate, new MonsterCandidate(actor.X, actor.Y, actor.Width, actor.Height, actor.Confidence));

    private static double Distance(MonsterCandidate a, MonsterCandidate b) => Math.Sqrt(
        Math.Pow(a.X + a.Width / 2 - b.X - b.Width / 2, 2)
        + Math.Pow(a.Y + a.Height / 2 - b.Y - b.Height / 2, 2));

    private sealed class Track(MonsterCandidate candidate, long lastFrame, int observations)
    {
        public MonsterCandidate Candidate { get; set; } = candidate;
        public long LastFrame { get; set; } = lastFrame;
        public int Observations { get; set; } = observations;
    }
}
