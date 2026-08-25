using Maple.Host.Preview;
using Maple.Host.Recognition;

namespace Maple.Host.Navigation;

public sealed record BgraTemplate(
    string Name,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels);

public sealed record MonsterCandidate(double X, double Y, double Width, double Height, double Confidence);

public sealed class MonsterTemplateMatcher(double referenceWidth = 1366)
{
    public IReadOnlyList<MonsterCandidate> Match(
        CapturedFrame frame,
        IReadOnlyList<BgraTemplate> templates,
        double threshold,
        MapMinimapRect? minimapRect)
    {
        List<MonsterCandidate> found = [];
        if (!double.IsFinite(referenceWidth) || referenceWidth <= 0
            || frame.Width <= 0 || frame.Height <= 0 || frame.Stride < frame.Width * 4
            || frame.BgraPixels.Length < (long)frame.Stride * frame.Height)
            return found;
        double scale = Math.Clamp(frame.Width / referenceWidth, 0.5, 2.5);
        foreach (BgraTemplate template in templates)
        {
            if (template.Width <= 0 || template.Height <= 0
                || template.Pixels.Length < (long)template.Width * template.Height * 4)
                continue;
            TemplateSample[] samples = BuildSamples(template);
            int scaledWidth = Math.Max(1, (int)Math.Round(template.Width * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(template.Height * scale));
            if (samples.Length < 8
                || scaledWidth > frame.Width || scaledHeight > frame.Height)
                continue;
            int maxY = Math.Max(0, (int)(frame.Height * 0.86) - scaledHeight);
            for (int y = 0; y <= maxY; y += 2)
            for (int x = 0; x <= frame.Width - scaledWidth; x += 2)
            {
                if (OverlapsMinimap(x, y, scaledWidth, scaledHeight, minimapRect)) continue;
                double score = Score(frame, samples, x, y, scale);
                if (score < threshold) continue;
                MonsterCandidate candidate = new(x, y, scaledWidth, scaledHeight, score);
                if (found.Any(item => IoU(item, candidate) > 0.3)) continue;
                found.Add(candidate);
                if (found.Count >= 24) return found;
            }
        }
        return found.OrderByDescending(candidate => candidate.Confidence).ToArray();
    }

    private static double Score(
        CapturedFrame frame,
        IReadOnlyList<TemplateSample> samples,
        int x,
        int y,
        double scale)
    {
        ReadOnlySpan<byte> target = frame.BgraPixels.Span;
        double sourceSum = 0, targetSum = 0;
        double sourceSquared = 0, targetSquared = 0, product = 0;
        int colorMatches = 0;
        foreach (TemplateSample sample in samples)
        {
            int localX = (int)Math.Round(sample.X * scale);
            int localY = (int)Math.Round(sample.Y * scale);
            int targetOffset = (y + localY) * frame.Stride + (x + localX) * 4;
            int distance = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                double sourceValue = sample.Bgr[channel];
                double targetValue = target[targetOffset + channel];
                sourceSum += sourceValue;
                targetSum += targetValue;
                sourceSquared += sourceValue * sourceValue;
                targetSquared += targetValue * targetValue;
                product += sourceValue * targetValue;
                distance += (int)Math.Abs(sourceValue - targetValue);
            }
            if (distance <= 140) colorMatches++;
        }
        if (colorMatches < samples.Count * 0.30) return 0;
        int count = samples.Count * 3;
        double covariance = product - sourceSum * targetSum / count;
        double sourceVariance = sourceSquared - sourceSum * sourceSum / count;
        double targetVariance = targetSquared - targetSum * targetSum / count;
        double denominator = Math.Sqrt(Math.Max(0, sourceVariance) * Math.Max(0, targetVariance));
        return denominator <= 0.000_001 ? 0 : Math.Clamp(covariance / denominator, -1, 1);
    }

    private static TemplateSample[] BuildSamples(BgraTemplate template)
    {
        ReadOnlySpan<byte> pixels = template.Pixels.Span;
        List<TemplateSample> opaque = [];
        for (int y = 0; y < template.Height; y++)
        for (int x = 0; x < template.Width; x++)
        {
            int offset = (y * template.Width + x) * 4;
            if (pixels[offset + 3] < 100) continue;
            opaque.Add(new TemplateSample(x, y, [pixels[offset], pixels[offset + 1], pixels[offset + 2]]));
        }
        if (opaque.Count <= 64) return [.. opaque];
        double step = opaque.Count / 64d;
        return Enumerable.Range(0, 64).Select(index => opaque[(int)(index * step)]).ToArray();
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

    private sealed record TemplateSample(int X, int Y, byte[] Bgr);
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
