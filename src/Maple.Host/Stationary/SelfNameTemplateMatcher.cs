using Maple.Host.Preview;

namespace Maple.Host.Stationary;

public sealed class SelfNameTemplateMatcher
{
    private const int MaximumSamples = 128;

    public SelfNameMatch Match(
        CapturedFrame frame,
        ReadOnlyMemory<byte> templatePixels,
        int templateWidth,
        int templateHeight,
        FrameRect searchArea)
    {
        if (templateWidth <= 0 || templateHeight <= 0 ||
            templatePixels.Length != templateWidth * templateHeight * 4 ||
            !searchArea.IsInside(frame.Width, frame.Height) ||
            templateWidth > searchArea.Width || templateHeight > searchArea.Height)
            return Missing(frame.Sequence, "VISUAL_NAME_TEMPLATE_INVALID");

        TemplateSample[] samples = BuildSamples(templatePixels.Span, templateWidth, templateHeight);
        if (samples.Length == 0) return Missing(frame.Sequence, "VISUAL_NAME_TEMPLATE_LOW_TEXTURE");

        Candidate best = FindBest(frame, samples, templateWidth, templateHeight, searchArea, null);
        if (best.Score < 0) return Missing(frame.Sequence, "VISUAL_NAME_NOT_FOUND");
        int exclusionX = Math.Max(2, templateWidth / 2);
        int exclusionY = Math.Max(2, templateHeight / 2);
        Candidate second = FindBest(
            frame,
            samples,
            templateWidth,
            templateHeight,
            searchArea,
            candidate => Math.Abs(candidate.X - best.X) < exclusionX && Math.Abs(candidate.Y - best.Y) < exclusionY);
        return new SelfNameMatch(
            true,
            "VISUAL_NAME_CANDIDATE",
            frame.Sequence,
            best.Score,
            Math.Max(0, second.Score),
            best.X + templateWidth / 2d,
            best.Y + templateHeight / 2d,
            second.Score >= 0 ? second.X + templateWidth / 2d : double.NaN,
            second.Score >= 0 ? second.Y + templateHeight / 2d : double.NaN);
    }

    private static Candidate FindBest(
        CapturedFrame frame,
        IReadOnlyList<TemplateSample> samples,
        int templateWidth,
        int templateHeight,
        FrameRect search,
        Func<Candidate, bool>? excluded)
    {
        Candidate best = new(0, 0, -1);
        int maxX = search.Right - templateWidth;
        int maxY = search.Bottom - templateHeight;
        for (int y = search.Y; y <= maxY; y++)
        for (int x = search.X; x <= maxX; x++)
        {
            Candidate candidate = new(x, y, 0);
            if (excluded?.Invoke(candidate) == true) continue;
            double score = Score(frame, samples, x, y);
            if (score > best.Score) best = new Candidate(x, y, score);
        }
        return best;
    }

    private static double Score(
        CapturedFrame frame,
        IReadOnlyList<TemplateSample> samples,
        int originX,
        int originY)
    {
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        double rawScoreSum = 0;
        double robustScoreSum = 0;
        foreach (TemplateSample sample in samples)
        {
            int offset = (originY + sample.Y) * frame.Stride + (originX + sample.X) * 4;
            int colorDifference = Math.Abs(pixels[offset] - sample.B) +
                Math.Abs(pixels[offset + 1] - sample.G) +
                Math.Abs(pixels[offset + 2] - sample.R);
            int center = Luminance(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
            int rightOffset = offset + 4;
            int downOffset = offset + frame.Stride;
            int right = Luminance(pixels[rightOffset], pixels[rightOffset + 1], pixels[rightOffset + 2]);
            int down = Luminance(pixels[downOffset], pixels[downOffset + 1], pixels[downOffset + 2]);
            int edgeDifference = Math.Abs((center - right) - sample.RightDelta) +
                Math.Abs((center - down) - sample.DownDelta);
            double colorScore = 1d - colorDifference / (3d * 255d);
            double edgeScore = 1d - Math.Min(1d, edgeDifference / (2d * 255d));
            double featureScore = edgeScore * 0.8 + colorScore * 0.2;
            rawScoreSum += featureScore;
            robustScoreSum += Math.Max(0.75, featureScore);
        }
        double rawScore = rawScoreSum / samples.Count;
        double robustScore = robustScoreSum / samples.Count;
        return Math.Clamp(robustScore * 0.8 + rawScore * 0.2, 0, 1);
    }

    private static TemplateSample[] BuildSamples(ReadOnlySpan<byte> pixels, int width, int height)
    {
        int minimum = 255;
        int maximum = 0;
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            int luminance = Luminance(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
            minimum = Math.Min(minimum, luminance);
            maximum = Math.Max(maximum, luminance);
        }
        if (maximum - minimum < 16) return [];

        var edges = new List<(int X, int Y, int Strength, int RightDelta, int DownDelta)>();
        int strongest = 0;
        for (int y = 0; y < height - 1; y++)
        for (int x = 0; x < width - 1; x++)
        {
            int offset = (y * width + x) * 4;
            int rightOffset = offset + 4;
            int downOffset = offset + width * 4;
            int center = Luminance(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
            int rightDelta = center - Luminance(pixels[rightOffset], pixels[rightOffset + 1], pixels[rightOffset + 2]);
            int downDelta = center - Luminance(pixels[downOffset], pixels[downOffset + 1], pixels[downOffset + 2]);
            int strength = Math.Abs(rightDelta) + Math.Abs(downDelta);
            strongest = Math.Max(strongest, strength);
            edges.Add((x, y, strength, rightDelta, downDelta));
        }
        int threshold = Math.Max(12, (int)Math.Ceiling(strongest * 0.15));
        byte[] sourcePixels = pixels.ToArray();
        return edges
            .Where(edge => edge.Strength >= threshold)
            .OrderByDescending(edge => edge.Strength)
            .Take(MaximumSamples)
            .Select(edge =>
            {
                int offset = (edge.Y * width + edge.X) * 4;
                return new TemplateSample(
                    edge.X,
                    edge.Y,
                    sourcePixels[offset],
                    sourcePixels[offset + 1],
                    sourcePixels[offset + 2],
                    edge.RightDelta,
                    edge.DownDelta);
            })
            .ToArray();
    }

    private static int Luminance(byte b, byte g, byte r) => (b * 11 + g * 59 + r * 30) / 100;

    private static SelfNameMatch Missing(long sequence, string code) =>
        new(false, code, sequence, 0, 0, 0, 0, double.NaN, double.NaN);

    private readonly record struct Candidate(int X, int Y, double Score);
    private readonly record struct TemplateSample(
        int X,
        int Y,
        byte B,
        byte G,
        byte R,
        int RightDelta,
        int DownDelta);
}
