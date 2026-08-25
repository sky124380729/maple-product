using Maple.Host.Navigation;
using Maple.Host.Preview;
using Maple.Host.Recognition;

namespace Maple.Host.Tests.Navigation;

public sealed class MonsterTemplateMatcherTests
{
    [Fact]
    public void Authorizes_package_template_seen_in_two_distinct_frames()
    {
        BgraTemplate template = Template();
        CapturedFrame frame = FrameWithTemplate(template, sequence: 1, x: 40, y: 20);
        MonsterTemplateMatcher matcher = new(referenceWidth: 100);
        MonsterTargetStabilizer stabilizer = new();

        IReadOnlyList<MonsterCandidate> first = matcher.Match(frame, [template], 0.8, null);
        Assert.Empty(stabilizer.Update(1, first, [], []));
        IReadOnlyList<MonsterCandidate> second = matcher.Match(frame with { Sequence = 2 }, [template], 0.8, null);

        Assert.Single(stabilizer.Update(2, second, [], []));
    }

    [Fact]
    public void Matches_package_template_in_uniformly_scaled_physical_frame()
    {
        BgraTemplate template = Template();
        CapturedFrame logical = FrameWithTemplate(template, sequence: 3, x: 40, y: 20);
        CapturedFrame physical = Scale(logical, 1.5);
        MonsterTemplateMatcher matcher = new(referenceWidth: 100);

        MonsterCandidate match = Assert.Single(matcher.Match(physical, [template], 0.8, null));

        Assert.InRange(match.X, 59, 61);
        Assert.InRange(match.Y, 29, 31);
        Assert.Equal(6, match.Width);
        Assert.Equal(6, match.Height);
    }

    [Fact]
    public void Generic_recognition_alone_never_authorizes_attack()
    {
        MonsterTargetStabilizer stabilizer = new();
        RecognitionTarget generic = new(40, 20, 4, 4, "monster", 0.99);

        Assert.Empty(stabilizer.Update(1, [], [generic], []));
        Assert.Empty(stabilizer.Update(2, [], [generic], []));
    }

    [Fact]
    public void Excludes_template_candidate_overlapping_player()
    {
        MonsterTargetStabilizer stabilizer = new();
        MonsterCandidate candidate = new(40, 20, 4, 4, 0.95);
        RecognitionTarget player = new(39, 19, 6, 6, "player", 0.9);

        stabilizer.Update(1, [candidate], [], [player]);

        Assert.Empty(stabilizer.Update(2, [candidate], [], [player]));
    }

    [Fact]
    public void Ignores_truncated_template_pixels_without_faulting_observation_loop()
    {
        CapturedFrame frame = FrameWithTemplate(Template(), sequence: 1, x: 40, y: 20);
        BgraTemplate truncated = new("broken.png", 10, 10, new byte[8]);

        IReadOnlyList<MonsterCandidate> matches = new MonsterTemplateMatcher(referenceWidth: 100)
            .Match(frame, [truncated], 0.8, null);

        Assert.Empty(matches);
    }

    private static BgraTemplate Template()
    {
        byte[] pixels = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++)
        {
            pixels[i * 4] = 20;
            pixels[i * 4 + 1] = (byte)(120 + i);
            pixels[i * 4 + 2] = (byte)(210 - i);
            pixels[i * 4 + 3] = 255;
        }
        return new BgraTemplate("mob.png", 4, 4, pixels);
    }

    private static CapturedFrame FrameWithTemplate(BgraTemplate template, long sequence, int x, int y)
    {
        const int width = 100, height = 60;
        byte[] pixels = new byte[width * height * 4];
        for (int index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
        for (int ty = 0; ty < template.Height; ty++)
        for (int tx = 0; tx < template.Width; tx++)
            template.Pixels.Span.Slice((ty * template.Width + tx) * 4, 4)
                .CopyTo(pixels.AsSpan(((y + ty) * width + x + tx) * 4, 4));
        return new CapturedFrame(width, height, width * 4, pixels, sequence * 10, sequence);
    }

    private static CapturedFrame Scale(CapturedFrame source, double scale)
    {
        int width = (int)Math.Round(source.Width * scale);
        int height = (int)Math.Round(source.Height * scale);
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sourceX = Math.Min(source.Width - 1, (int)(x / scale));
            int sourceY = Math.Min(source.Height - 1, (int)(y / scale));
            source.BgraPixels.Span.Slice(sourceY * source.Stride + sourceX * 4, 4)
                .CopyTo(pixels.AsSpan((y * width + x) * 4, 4));
        }
        return new CapturedFrame(width, height, width * 4, pixels, source.CapturedAtMonoMs, source.Sequence);
    }
}
