using Maple.Host.Preview;

namespace Maple.Host.Stationary;

public sealed class CharacterAppearanceCalibrator
{
    private const int MaximumTemplates = 8;
    private const int AlignmentRadiusAtReference = 12;
    private const double ReferenceFrameWidth = 1366d;
    private const double MinimumAlignmentScore = 0.60;
    private const double DuplicateScore = 0.97;

    private readonly int frameWidth;
    private readonly int frameHeight;
    private readonly FrameRect source;
    private readonly List<byte[]> templates = [];
    private readonly SelfAppearanceTemplateMatcher matcher = new();
    private long lastSequence;

    public CharacterAppearanceCalibrator(CapturedFrame frozenFrame, FrameRect source)
    {
        ArgumentNullException.ThrowIfNull(frozenFrame);
        if (!source.IsInside(frozenFrame.Width, frozenFrame.Height))
            throw new ArgumentOutOfRangeException(nameof(source));

        frameWidth = frozenFrame.Width;
        frameHeight = frozenFrame.Height;
        this.source = source;
        lastSequence = frozenFrame.Sequence;
        byte[] frozenTemplate = Crop(frozenFrame, source);
        templates.Add(frozenTemplate);
    }

    public int TemplateCount => templates.Count;
    public int ObservedNewFrameCount { get; private set; }
    public bool ViewportMismatchDetected { get; private set; }

    public bool TryAdd(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != frameWidth || frame.Height != frameHeight)
        {
            ViewportMismatchDetected = true;
            return false;
        }
        if (frame.Sequence <= lastSequence)
            return false;
        lastSequence = frame.Sequence;
        ObservedNewFrameCount++;
        if (templates.Count >= MaximumTemplates) return false;

        FrameRect search = CreateAlignmentSearchArea();
        SelfNameMatch aligned = matcher.Match(
            frame,
            templates.ToArray(),
            source.Width,
            source.Height,
            search);
        if (!aligned.HasCandidate || aligned.BestScore < MinimumAlignmentScore)
            return false;

        int left = (int)Math.Round(aligned.CenterX - source.Width / 2d);
        int top = (int)Math.Round(aligned.CenterY - source.Height / 2d);
        var alignedSource = new FrameRect(left, top, source.Width, source.Height);
        if (!alignedSource.IsInside(frameWidth, frameHeight)) return false;

        byte[] candidate = Crop(frame, alignedSource);
        if (templates.Any(existing => Similarity(candidate, existing) >= DuplicateScore))
            return false;

        templates.Add(candidate);
        return true;
    }

    public VisualCharacterTemplateBank Complete(DateTimeOffset? capturedAtUtc = null) => new(
        source,
        source.Width,
        source.Height,
        templates.Select(template => template.ToArray()).ToArray(),
        MatcherVersion: 1,
        CapturedAtUtc: capturedAtUtc);

    private FrameRect CreateAlignmentSearchArea()
    {
        int radius = Math.Max(
            1,
            (int)Math.Ceiling(AlignmentRadiusAtReference * frameWidth / ReferenceFrameWidth));
        int left = Math.Max(0, source.X - radius);
        int top = Math.Max(0, source.Y - radius);
        int right = Math.Min(frameWidth, source.Right + radius);
        int bottom = Math.Min(frameHeight, source.Bottom + radius);
        return new FrameRect(left, top, right - left, bottom - top);
    }

    private double Similarity(byte[] candidate, byte[] existing)
    {
        return Math.Max(
            PixelSimilarity(candidate, existing, mirrored: false),
            PixelSimilarity(candidate, existing, mirrored: true));
    }

    private double PixelSimilarity(byte[] candidate, byte[] existing, bool mirrored)
    {
        long difference = 0;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            int candidateOffset = (y * source.Width + x) * 4;
            int existingX = mirrored ? source.Width - 1 - x : x;
            int existingOffset = (y * source.Width + existingX) * 4;
            difference += Math.Abs(candidate[candidateOffset] - existing[existingOffset]);
            difference += Math.Abs(candidate[candidateOffset + 1] - existing[existingOffset + 1]);
            difference += Math.Abs(candidate[candidateOffset + 2] - existing[existingOffset + 2]);
        }
        double maximum = source.Width * source.Height * 3d * 255d;
        return 1d - difference / maximum;
    }

    private static byte[] Crop(CapturedFrame frame, FrameRect area) =>
        CapturedFrameCropper.Crop(frame, area.X, area.Y, area.Width, area.Height)
            .BgraPixels.ToArray();
}
