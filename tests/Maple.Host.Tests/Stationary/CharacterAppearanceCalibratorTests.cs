using Maple.Host.Preview;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class CharacterAppearanceCalibratorTests
{
    private static readonly FrameRect Source = new(64, 30, 32, 40);

    [Fact]
    public void Always_keeps_the_frozen_source_as_the_first_template()
    {
        byte[] appearance = SelfAppearanceTemplateMatcherTests.Template(0);
        CapturedFrame frozen = SelfAppearanceTemplateMatcherTests.Frame((Source.X, Source.Y, appearance));

        VisualCharacterTemplateBank bank = new CharacterAppearanceCalibrator(frozen, Source).Complete();

        Assert.Single(bank.TemplatesBgra);
        Assert.Equal(appearance, bank.TemplatesBgra[0]);
        Assert.Equal(Source, bank.Source);
        Assert.Equal(1, bank.MatcherVersion);
    }

    [Fact]
    public void Aligns_a_distinct_animation_within_twelve_scaled_pixels_and_discards_duplicates()
    {
        byte[] idle = SelfAppearanceTemplateMatcherTests.Template(0);
        byte[] attack = OccludedVariant(idle, block: 0);
        CapturedFrame frozen = SelfAppearanceTemplateMatcherTests.Frame((Source.X, Source.Y, idle));
        var calibrator = new CharacterAppearanceCalibrator(frozen, Source);

        bool duplicateAdded = calibrator.TryAdd(
            SelfAppearanceTemplateMatcherTests.Frame((Source.X + 2, Source.Y + 2, idle)) with { Sequence = 2 });
        bool animationAdded = calibrator.TryAdd(
            SelfAppearanceTemplateMatcherTests.Frame((Source.X + 2, Source.Y, attack)) with { Sequence = 3 });

        Assert.False(duplicateAdded);
        Assert.True(animationAdded);
        Assert.Equal(2, calibrator.TemplateCount);
    }

    [Fact]
    public void Accepts_a_distinct_facing_pose_below_the_runtime_acquisition_score()
    {
        byte[] idle = SelfAppearanceTemplateMatcherTests.Template(0);
        byte[] oppositeFacing = InvertColors(idle);
        CapturedFrame frozen = SelfAppearanceTemplateMatcherTests.Frame((Source.X, Source.Y, idle));
        CapturedFrame facingFrame = SelfAppearanceTemplateMatcherTests.Frame(
            (Source.X, Source.Y, oppositeFacing)) with { Sequence = 2 };
        double score = new SelfAppearanceTemplateMatcher().Match(
            facingFrame,
            [idle],
            Source.Width,
            Source.Height,
            Source).BestScore;
        var calibrator = new CharacterAppearanceCalibrator(frozen, Source);

        bool added = calibrator.TryAdd(facingFrame);

        Assert.InRange(score, 0.60, 0.819999);
        Assert.True(added);
    }

    [Fact]
    public void Caps_the_fixed_template_bank_at_eight()
    {
        byte[] idle = SelfAppearanceTemplateMatcherTests.Template(0);
        CapturedFrame frozen = SelfAppearanceTemplateMatcherTests.Frame((Source.X, Source.Y, idle));
        var calibrator = new CharacterAppearanceCalibrator(frozen, Source);

        for (int animation = 1; animation <= 100; animation++)
            calibrator.TryAdd(
                SelfAppearanceTemplateMatcherTests.Frame(
                    (Source.X, Source.Y, SelfAppearanceTemplateMatcherTests.Template(animation * 37)))
                    with { Sequence = animation + 1 });

        Assert.Equal(8, calibrator.TemplateCount);
    }

    [Fact]
    public void Rejects_frames_from_another_viewport_and_invalid_source_rectangles()
    {
        byte[] idle = SelfAppearanceTemplateMatcherTests.Template(0);
        CapturedFrame frozen = SelfAppearanceTemplateMatcherTests.Frame((Source.X, Source.Y, idle));
        var calibrator = new CharacterAppearanceCalibrator(frozen, Source);
        byte[] otherPixels = new byte[221 * 100 * 4];
        var otherViewport = new CapturedFrame(221, 100, 221 * 4, otherPixels, Environment.TickCount64, 2);

        Assert.False(calibrator.TryAdd(otherViewport));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CharacterAppearanceCalibrator(frozen, new FrameRect(210, 90, 32, 40)));
    }

    [Fact]
    public void Complete_returns_deep_clones_that_cannot_mutate_calibration_state()
    {
        byte[] idle = SelfAppearanceTemplateMatcherTests.Template(0);
        CapturedFrame frozen = SelfAppearanceTemplateMatcherTests.Frame((Source.X, Source.Y, idle));
        var calibrator = new CharacterAppearanceCalibrator(frozen, Source);

        VisualCharacterTemplateBank first = calibrator.Complete();
        first.TemplatesBgra[0][0] ^= 0xFF;
        VisualCharacterTemplateBank second = calibrator.Complete();

        Assert.Equal(idle[0], second.TemplatesBgra[0][0]);
    }

    [Fact]
    public void Tracks_new_frame_progress_and_remembers_any_viewport_mismatch()
    {
        byte[] idle = SelfAppearanceTemplateMatcherTests.Template(0);
        CapturedFrame frozen = SelfAppearanceTemplateMatcherTests.Frame((Source.X, Source.Y, idle));
        var calibrator = new CharacterAppearanceCalibrator(frozen, Source);
        CapturedFrame next = SelfAppearanceTemplateMatcherTests.Frame((Source.X, Source.Y, idle)) with { Sequence = 2 };
        byte[] otherPixels = new byte[221 * 100 * 4];
        var wrongViewport = new CapturedFrame(221, 100, 221 * 4, otherPixels, 20, 3);

        calibrator.TryAdd(next);
        calibrator.TryAdd(next);
        calibrator.TryAdd(wrongViewport);

        Assert.Equal(1, calibrator.ObservedNewFrameCount);
        Assert.True(calibrator.ViewportMismatchDetected);
    }

    private static byte[] OccludedVariant(byte[] source, int block)
    {
        const int width = 32, height = 40;
        byte[] variant = source.ToArray();
        int blockWidth = 6;
        int startX = block * 4 % (width - blockWidth);
        int startY = block * 3 % 12;
        for (int y = startY; y < Math.Min(height, startY + 24); y++)
        for (int x = startX; x < startX + blockWidth; x++)
        {
            int offset = (y * width + x) * 4;
            variant[offset] = (byte)(240 - block * 7);
            variant[offset + 1] = (byte)(10 + block * 11);
            variant[offset + 2] = (byte)(180 - block * 9);
        }
        return variant;
    }

    private static byte[] InvertColors(byte[] source)
    {
        byte[] result = source.ToArray();
        for (int offset = 0; offset < result.Length; offset += 4)
        {
            result[offset] = (byte)(255 - result[offset]);
            result[offset + 1] = (byte)(255 - result[offset + 1]);
            result[offset + 2] = (byte)(255 - result[offset + 2]);
        }
        return result;
    }
}
