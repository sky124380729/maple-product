using Maple.Host.Preview;

namespace Maple.Host.Stationary;

public sealed class SelfAppearanceTemplateMatcher
{
    private const int MaximumSamplesPerTemplate = 128;
    private const int RefinementRadiusPx = 2;
    private const double StructureCorrelationWeight = 0.12;
    private IReadOnlyList<byte[]>? cachedTemplates;
    private int cachedWidth;
    private int cachedHeight;
    private TemplateSample[][] cachedSampleBanks = [];

    public SelfNameMatch Match(
        CapturedFrame frame,
        IReadOnlyList<byte[]> templates,
        int templateWidth,
        int templateHeight,
        FrameRect searchArea,
        int coarseSampleLimit = 0)
    {
        if (templates is null || templates.Count is < 1 or > 8 ||
            templateWidth <= 0 || templateHeight <= 0 ||
            coarseSampleLimit is < 0 or > MaximumSamplesPerTemplate ||
            !searchArea.IsInside(frame.Width, frame.Height) ||
            templateWidth > searchArea.Width || templateHeight > searchArea.Height)
            return Missing(frame.Sequence, "VISUAL_CHARACTER_TEMPLATE_INVALID");

        TemplateSample[][] sampleBanks = GetSampleBanks(templates, templateWidth, templateHeight);
        if (sampleBanks.Length == 0)
            return Missing(frame.Sequence, "VISUAL_CHARACTER_TEMPLATE_LOW_TEXTURE");

        Candidate best = FindBest(
            frame,
            sampleBanks,
            templateWidth,
            templateHeight,
            searchArea,
            null,
            coarseSampleLimit);
        if (best.Score < 0) return Missing(frame.Sequence, "VISUAL_CHARACTER_NOT_FOUND");
        if (coarseSampleLimit > 0)
            best = Refine(
                frame,
                sampleBanks,
                templateWidth,
                templateHeight,
                searchArea,
                best,
                null);
        double sameTargetRadiusX = Math.Max(2d, templateWidth / 2d);
        double sameTargetRadiusY = Math.Max(2d, templateHeight / 2d);
        bool IsSameTarget(Candidate candidate)
        {
            double deltaX = candidate.X - best.X;
            double deltaY = candidate.Y - best.Y;
            return Math.Abs(deltaX) < sameTargetRadiusX &&
                Math.Abs(deltaY) < sameTargetRadiusY;
        }
        Candidate second = FindBest(
            frame,
            sampleBanks,
            templateWidth,
            templateHeight,
            searchArea,
            IsSameTarget,
            coarseSampleLimit);
        if (coarseSampleLimit > 0 && second.Score >= 0)
            second = Refine(
                frame,
                sampleBanks,
                templateWidth,
                templateHeight,
                searchArea,
                second,
                IsSameTarget);
        if (second.Score > best.Score) (best, second) = (second, best);
        return new SelfNameMatch(
            true,
            "VISUAL_CHARACTER_CANDIDATE",
            frame.Sequence,
            best.Score,
            Math.Max(0, second.Score),
            best.X + templateWidth / 2d,
            best.Y + templateHeight / 2d,
            second.Score >= 0 ? second.X + templateWidth / 2d : double.NaN,
            second.Score >= 0 ? second.Y + templateHeight / 2d : double.NaN);
    }

    private TemplateSample[][] GetSampleBanks(
        IReadOnlyList<byte[]> templates,
        int templateWidth,
        int templateHeight)
    {
        if (ReferenceEquals(templates, cachedTemplates) &&
            templateWidth == cachedWidth &&
            templateHeight == cachedHeight)
            return cachedSampleBanks;

        int expectedLength = templateWidth * templateHeight * 4;
        var banks = new List<TemplateSample[]>(templates.Count * 2);
        foreach (byte[]? template in templates)
        {
            if (template is null || template.Length != expectedLength) return [];
            TemplateSample[] original = BuildSamples(template, templateWidth, templateHeight);
            TemplateSample[] mirrored = BuildSamples(
                Mirror(template, templateWidth, templateHeight),
                templateWidth,
                templateHeight);
            if (original.Length == 0 || mirrored.Length == 0) return [];
            banks.Add(original);
            banks.Add(mirrored);
        }

        cachedTemplates = templates;
        cachedWidth = templateWidth;
        cachedHeight = templateHeight;
        cachedSampleBanks = [.. banks];
        return cachedSampleBanks;
    }

    private static Candidate FindBest(
        CapturedFrame frame,
        IReadOnlyList<TemplateSample[]> sampleBanks,
        int templateWidth,
        int templateHeight,
        FrameRect search,
        Func<Candidate, bool>? excluded,
        int sampleLimit)
    {
        Candidate best = new(0, 0, -1);
        int maxX = search.Right - templateWidth;
        int maxY = search.Bottom - templateHeight;
        for (int y = search.Y; y <= maxY; y++)
        for (int x = search.X; x <= maxX; x++)
        {
            Candidate candidate = new(x, y, 0);
            if (excluded?.Invoke(candidate) == true) continue;
            double score = 0;
            foreach (TemplateSample[] samples in sampleBanks)
                score = Math.Max(score, Score(frame, samples, x, y, sampleLimit));
            if (score > best.Score) best = new Candidate(x, y, score);
        }
        return best;
    }

    private static Candidate Refine(
        CapturedFrame frame,
        IReadOnlyList<TemplateSample[]> sampleBanks,
        int templateWidth,
        int templateHeight,
        FrameRect fullSearch,
        Candidate coarse,
        Func<Candidate, bool>? excluded)
    {
        int maximumX = fullSearch.Right - templateWidth;
        int maximumY = fullSearch.Bottom - templateHeight;
        int left = Math.Max(fullSearch.X, coarse.X - RefinementRadiusPx);
        int top = Math.Max(fullSearch.Y, coarse.Y - RefinementRadiusPx);
        int right = Math.Min(maximumX, coarse.X + RefinementRadiusPx);
        int bottom = Math.Min(maximumY, coarse.Y + RefinementRadiusPx);
        var refinement = new FrameRect(
            left,
            top,
            right - left + templateWidth,
            bottom - top + templateHeight);
        return FindBest(
            frame,
            sampleBanks,
            templateWidth,
            templateHeight,
            refinement,
            excluded,
            0);
    }

    private static double Score(
        CapturedFrame frame,
        IReadOnlyList<TemplateSample> samples,
        int originX,
        int originY,
        int sampleLimit)
    {
        ReadOnlySpan<byte> pixels = frame.BgraPixels.Span;
        double rawScoreSum = 0;
        double robustScoreSum = 0;
        double templateLuminanceSum = 0;
        double candidateLuminanceSum = 0;
        double templateLuminanceSquaredSum = 0;
        double candidateLuminanceSquaredSum = 0;
        double luminanceProductSum = 0;
        int candidateLuminanceMinimum = 255;
        int candidateLuminanceMaximum = 0;
        int sampleCount = sampleLimit > 0 ? Math.Min(sampleLimit, samples.Count) : samples.Count;
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            TemplateSample sample = samples[sampleIndex];
            int offset = (originY + sample.Y) * frame.Stride + (originX + sample.X) * 4;
            int colorDifference = Math.Abs(pixels[offset] - sample.B) +
                Math.Abs(pixels[offset + 1] - sample.G) +
                Math.Abs(pixels[offset + 2] - sample.R);
            int templateLuminance = Luminance(sample.B, sample.G, sample.R);
            int center = Luminance(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
            int rightOffset = offset + 4;
            int downOffset = offset + frame.Stride;
            int right = Luminance(pixels[rightOffset], pixels[rightOffset + 1], pixels[rightOffset + 2]);
            int down = Luminance(pixels[downOffset], pixels[downOffset + 1], pixels[downOffset + 2]);
            candidateLuminanceMinimum = Math.Min(candidateLuminanceMinimum, Math.Min(center, Math.Min(right, down)));
            candidateLuminanceMaximum = Math.Max(candidateLuminanceMaximum, Math.Max(center, Math.Max(right, down)));
            int edgeDifference = Math.Abs((center - right) - sample.RightDelta) +
                Math.Abs((center - down) - sample.DownDelta);
            double colorScore = 1d - colorDifference / (3d * 255d);
            double edgeScore = 1d - Math.Min(1d, edgeDifference / (2d * 255d));
            double featureScore = edgeScore * 0.8 + colorScore * 0.2;
            rawScoreSum += featureScore;
            robustScoreSum += Math.Max(0.75, featureScore);
            templateLuminanceSum += templateLuminance;
            candidateLuminanceSum += center;
            templateLuminanceSquaredSum += templateLuminance * templateLuminance;
            candidateLuminanceSquaredSum += center * center;
            luminanceProductSum += templateLuminance * center;
        }
        if (candidateLuminanceMaximum - candidateLuminanceMinimum < 16) return 0;

        double rawScore = rawScoreSum / sampleCount;
        double robustScore = robustScoreSum / sampleCount;
        double count = sampleCount;
        double correlationNumerator = count * luminanceProductSum -
            templateLuminanceSum * candidateLuminanceSum;
        double correlationDenominator = Math.Sqrt(
            Math.Max(0, count * templateLuminanceSquaredSum -
                templateLuminanceSum * templateLuminanceSum) *
            Math.Max(0, count * candidateLuminanceSquaredSum -
                candidateLuminanceSum * candidateLuminanceSum));
        double structureCorrelation = correlationDenominator > 0.0001
            ? Math.Clamp(correlationNumerator / correlationDenominator, -1, 1)
            : 0;
        double occlusionTolerantScore = robustScore * 0.8 + rawScore * 0.2;
        double structureEvidence = Math.Max(0, structureCorrelation);
        return Math.Clamp(
            (occlusionTolerantScore + StructureCorrelationWeight * structureEvidence) /
                (1 + StructureCorrelationWeight),
            0,
            1);
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

        var edges = new List<EdgeSample>();
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
            edges.Add(new EdgeSample(x, y, strength, rightDelta, downDelta));
        }

        int threshold = Math.Max(12, (int)Math.Ceiling(strongest * 0.15));
        byte[] sourcePixels = pixels.ToArray();
        return edges
            .Where(edge => edge.Strength >= threshold)
            .OrderByDescending(edge => edge.Strength)
            .Take(MaximumSamplesPerTemplate)
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

    private static byte[] Mirror(byte[] source, int width, int height)
    {
        byte[] mirrored = new byte[source.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            source.AsSpan((y * width + x) * 4, 4)
                .CopyTo(mirrored.AsSpan((y * width + width - 1 - x) * 4, 4));
        return mirrored;
    }

    private static int Luminance(byte b, byte g, byte r) => (b * 11 + g * 59 + r * 30) / 100;

    private static SelfNameMatch Missing(long sequence, string code) =>
        new(false, code, sequence, 0, 0, 0, 0, double.NaN, double.NaN);

    private readonly record struct Candidate(int X, int Y, double Score);
    private readonly record struct EdgeSample(
        int X,
        int Y,
        int Strength,
        int RightDelta,
        int DownDelta);
    private readonly record struct TemplateSample(
        int X,
        int Y,
        byte B,
        byte G,
        byte R,
        int RightDelta,
        int DownDelta);
}
