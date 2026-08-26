using Maple.Host.Preview;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class SelfAppearanceTemplateMatcherTests
{
    private const int TemplateWidth = 32;
    private const int TemplateHeight = 40;

    [Fact]
    public void Matches_a_second_calibrated_animation_template()
    {
        byte[] idle = Template(seed: 0);
        byte[] attack = Template(seed: 37);
        CapturedFrame frame = Frame((64, 30, attack));

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [idle, attack],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(52, 20, 56, 60));

        Assert.InRange(match.BestScore, 0.99, 1.0);
        Assert.Equal(80, match.CenterX);
        Assert.Equal(50, match.CenterY);
    }

    [Fact]
    public void Matches_the_horizontal_mirror_without_storing_a_second_copy()
    {
        byte[] original = Template(seed: 0);
        CapturedFrame frame = Frame((64, 30, Mirror(original)));

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [original],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(52, 20, 56, 60));

        Assert.InRange(match.BestScore, 0.99, 1.0);
        Assert.Equal(80, match.CenterX);
    }

    [Fact]
    public void Keeps_a_unique_candidate_with_one_fifth_of_the_patch_occluded()
    {
        byte[] appearance = Template(seed: 0);
        CapturedFrame frame = Occlude(Frame((64, 30, appearance)), new FrameRect(64, 30, 6, TemplateHeight));

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [appearance],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(52, 20, 56, 60));

        Assert.InRange(match.BestScore, 0.88, 1.0);
        Assert.True(match.BestScore - match.SecondBestScore >= 0.06);
    }

    [Fact]
    public void Never_considers_an_exact_distant_copy_outside_the_local_search()
    {
        byte[] appearance = Template(seed: 0);
        byte[] locallyOccluded = appearance.ToArray();
        for (int y = 0; y < TemplateHeight; y++)
        for (int x = 0; x < 4; x++)
        {
            int offset = (y * TemplateWidth + x) * 4;
            locallyOccluded[offset] = 0;
            locallyOccluded[offset + 1] = 0;
            locallyOccluded[offset + 2] = 0;
        }
        CapturedFrame frame = Frame((64, 30, locallyOccluded), (145, 30, appearance));

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [appearance],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(52, 20, 56, 60));

        Assert.Equal(80, match.CenterX);
        Assert.True(match.BestScore < 1.0);
    }

    [Fact]
    public void Reports_a_spatially_distinct_local_second_candidate()
    {
        byte[] appearance = Template(seed: 0);
        CapturedFrame frame = Frame((54, 30, appearance), (92, 30, appearance));

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [appearance],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(48, 20, 90, 60));

        Assert.InRange(match.BestScore, 0.99, 1.0);
        Assert.InRange(match.SecondBestScore, 0.99, 1.0);
        Assert.True(Math.Abs(match.CenterX - match.SecondCenterX) >= TemplateWidth / 3d);
    }

    [Fact]
    public void Suppresses_overlapping_alignments_of_the_same_appearance_as_a_second_candidate()
    {
        byte[] appearance = TopStripTemplate();
        CapturedFrame frame = FrameWithOverlappingVerticalPatches(appearance, 20, 31);

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [appearance],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(34, 18, 44, 56));

        Assert.InRange(match.BestScore, 0.99, 1.0);
        Assert.Equal(0, match.SecondBestScore);
        Assert.True(double.IsNaN(match.SecondCenterX));
        Assert.True(double.IsNaN(match.SecondCenterY));
    }

    [Fact]
    public void Uniform_missing_character_patch_scores_below_the_tracking_threshold()
    {
        byte[] appearance = Template(seed: 0);
        CapturedFrame frame = Frame();

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [appearance],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(52, 20, 56, 60));

        Assert.True(
            match.BestScore < VisualStationaryObservationSession.CharacterTrackingScoreThreshold,
            $"Uniform background scored {match.BestScore:F4}.");
    }

    [Fact]
    public void Sparse_feature_search_refines_an_arbitrary_pixel_candidate_exactly()
    {
        byte[] appearance = Template(seed: 0);
        CapturedFrame frame = Frame((65, 31, appearance));

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [appearance],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(48, 20, 100, 70),
            coarseSampleLimit: 16);

        Assert.InRange(match.BestScore, 0.99, 1.0);
        Assert.Equal(81, match.CenterX);
        Assert.Equal(51, match.CenterY);
    }

    [Fact]
    public void Sparse_feature_search_refines_two_spatial_candidates_for_ambiguity()
    {
        byte[] appearance = Template(seed: 0);
        CapturedFrame frame = Frame((55, 31, appearance), (99, 33, appearance));

        SelfNameMatch match = new SelfAppearanceTemplateMatcher().Match(
            frame,
            [appearance],
            TemplateWidth,
            TemplateHeight,
            new FrameRect(48, 20, 100, 70),
            coarseSampleLimit: 16);

        Assert.InRange(match.BestScore, 0.99, 1.0);
        Assert.InRange(match.SecondBestScore, 0.99, 1.0);
        Assert.True(Math.Abs(match.CenterX - match.SecondCenterX) >= TemplateWidth / 2d);
    }

    internal static byte[] Template(int seed)
    {
        byte[] pixels = new byte[TemplateWidth * TemplateHeight * 4];
        for (int y = 0; y < TemplateHeight; y++)
        for (int x = 0; x < TemplateWidth; x++)
        {
            int index = y * TemplateWidth + x;
            int offset = index * 4;
            pixels[offset] = (byte)(20 + (index * 7 + seed + x * x) % 220);
            pixels[offset + 1] = (byte)(25 + (index * 5 + seed + y * y) % 210);
            pixels[offset + 2] = (byte)(30 + (index * 11 + seed + x * y) % 205);
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    internal static CapturedFrame Frame(params (int X, int Y, byte[] Pixels)[] patches)
    {
        const int width = 220, height = 100;
        byte[] pixels = new byte[width * height * 4];
        for (int offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        foreach ((int x, int y, byte[] patch) in patches)
        {
            for (int row = 0; row < TemplateHeight; row++)
                patch.AsSpan(row * TemplateWidth * 4, TemplateWidth * 4)
                    .CopyTo(pixels.AsSpan(((y + row) * width + x) * 4, TemplateWidth * 4));
        }
        return new CapturedFrame(width, height, width * 4, pixels, Environment.TickCount64, 1);
    }

    internal static byte[] Mirror(byte[] source)
    {
        byte[] mirrored = new byte[source.Length];
        for (int y = 0; y < TemplateHeight; y++)
        for (int x = 0; x < TemplateWidth; x++)
            source.AsSpan((y * TemplateWidth + x) * 4, 4)
                .CopyTo(mirrored.AsSpan((y * TemplateWidth + TemplateWidth - 1 - x) * 4, 4));
        return mirrored;
    }

    private static byte[] TopStripTemplate()
    {
        byte[] pixels = new byte[TemplateWidth * TemplateHeight * 4];
        byte[] texture = Template(seed: 19);
        for (int row = 0; row < 8; row++)
            texture.AsSpan(row * TemplateWidth * 4, TemplateWidth * 4)
                .CopyTo(pixels.AsSpan(row * TemplateWidth * 4, TemplateWidth * 4));
        for (int offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        return pixels;
    }

    private static CapturedFrame FrameWithOverlappingVerticalPatches(byte[] patch, int firstY, int secondY)
    {
        const int width = 120, height = 100, x = 40;
        byte[] pixels = new byte[width * height * 4];
        for (int offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        foreach (int y in new[] { firstY, secondY })
        for (int row = 0; row < TemplateHeight; row++)
            patch.AsSpan(row * TemplateWidth * 4, TemplateWidth * 4)
                .CopyTo(pixels.AsSpan(((y + row) * width + x) * 4, TemplateWidth * 4));
        return new CapturedFrame(width, height, width * 4, pixels, 1, 1);
    }

    private static CapturedFrame Occlude(CapturedFrame frame, FrameRect area)
    {
        byte[] pixels = frame.BgraPixels.ToArray();
        for (int y = area.Y; y < area.Bottom; y++)
        for (int x = area.X; x < area.Right; x++)
        {
            int offset = y * frame.Stride + x * 4;
            pixels[offset] = 0;
            pixels[offset + 1] = 0;
            pixels[offset + 2] = 0;
        }
        return frame with { BgraPixels = pixels };
    }
}
