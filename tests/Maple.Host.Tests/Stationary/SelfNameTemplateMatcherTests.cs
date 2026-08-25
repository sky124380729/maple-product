using Maple.Host.Preview;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class SelfNameTemplateMatcherTests
{
    [Fact]
    public void Finds_a_unique_exact_name_template()
    {
        byte[] template = Template(6, 4);
        CapturedFrame frame = Frame(80, 40, template, 6, 4, (20, 12));

        SelfNameMatch match = new SelfNameTemplateMatcher().Match(
            frame, template, 6, 4, new FrameRect(0, 0, 80, 40));

        Assert.True(match.HasCandidate);
        Assert.InRange(match.BestScore, 0.999, 1.0);
        Assert.True(match.BestScore - match.SecondBestScore >= 0.06);
        Assert.Equal(23, match.CenterX);
        Assert.Equal(14, match.CenterY);
    }

    [Fact]
    public void Reports_an_ambiguous_second_peak_for_two_identical_names()
    {
        byte[] template = Template(6, 4);
        CapturedFrame frame = Frame(80, 40, template, 6, 4, (10, 12), (50, 12));

        SelfNameMatch match = new SelfNameTemplateMatcher().Match(
            frame, template, 6, 4, new FrameRect(0, 0, 80, 40));

        Assert.True(match.HasCandidate);
        Assert.InRange(match.BestScore, 0.999, 1.0);
        Assert.InRange(match.SecondBestScore, 0.999, 1.0);
        Assert.Contains(match.SecondCenterX, new[] { 13d, 53d });
        Assert.NotEqual(match.CenterX, match.SecondCenterX);
    }

    [Fact]
    public void Rejects_uniform_templates_without_scanning_the_frame()
    {
        byte[] uniform = new byte[6 * 4 * 4];

        SelfNameMatch match = new SelfNameTemplateMatcher().Match(
            Frame(80, 40), uniform, 6, 4, new FrameRect(0, 0, 80, 40));

        Assert.False(match.HasCandidate);
        Assert.Equal("VISUAL_NAME_TEMPLATE_LOW_TEXTURE", match.Code);
    }

    [Fact]
    public void Keeps_a_name_match_when_only_the_background_behind_letters_changes()
    {
        const int width = 12, height = 6;
        byte[] template = NameOnBackground(width, height, 20, 70, 30);
        byte[] movedName = NameOnBackground(width, height, 130, 35, 20);
        CapturedFrame frame = Frame(80, 40, movedName, width, height, (30, 14));

        SelfNameMatch match = new SelfNameTemplateMatcher().Match(
            frame, template, width, height, new FrameRect(0, 0, 80, 40));

        Assert.InRange(match.BestScore, 0.90, 1.0);
        Assert.Equal(36, match.CenterX);
    }

    [Fact]
    public void Keeps_a_unique_name_match_when_a_pet_occludes_one_fifth_of_the_template()
    {
        const int width = 20, height = 8;
        byte[] template = Template(width, height);
        CapturedFrame frame = Occlude(
            Frame(100, 50, template, width, height, (30, 16)),
            new FrameRect(46, 16, 4, height));

        SelfNameMatch match = new SelfNameTemplateMatcher().Match(
            frame, template, width, height, new FrameRect(0, 0, 100, 50));

        Assert.InRange(match.BestScore, 0.90, 1.0);
        Assert.True(match.BestScore - match.SecondBestScore >= 0.06);
        Assert.Equal(40, match.CenterX);
        Assert.Equal(20, match.CenterY);
    }

    [Fact]
    public void Does_not_promote_a_small_name_fragment_to_a_trusted_score()
    {
        const int width = 20, height = 8;
        byte[] template = Template(width, height);
        CapturedFrame frame = Occlude(
            Frame(100, 50, template, width, height, (30, 16)),
            new FrameRect(38, 16, 12, height));

        SelfNameMatch match = new SelfNameTemplateMatcher().Match(
            frame, template, width, height, new FrameRect(0, 0, 100, 50));

        Assert.True(match.BestScore < 0.90);
    }

    internal static byte[] Template(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < width * height; index++)
        {
            pixels[index * 4] = (byte)(20 + index * 7 % 220);
            pixels[index * 4 + 1] = (byte)(230 - index * 5 % 200);
            pixels[index * 4 + 2] = (byte)(40 + index * 11 % 210);
            pixels[index * 4 + 3] = 255;
        }
        return pixels;
    }

    internal static CapturedFrame Frame(
        int width,
        int height,
        byte[]? template = null,
        int templateWidth = 0,
        int templateHeight = 0,
        params (int X, int Y)[] positions)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        if (template is not null)
        {
            foreach ((int x, int y) in positions)
            for (int row = 0; row < templateHeight; row++)
            for (int column = 0; column < templateWidth; column++)
                template.AsSpan((row * templateWidth + column) * 4, 4)
                    .CopyTo(pixels.AsSpan(((y + row) * width + x + column) * 4, 4));
        }
        return new CapturedFrame(width, height, width * 4, pixels, 100, 1);
    }

    private static byte[] NameOnBackground(int width, int height, byte b, byte g, byte r)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            bool glyph = y is 1 or 4 && x is >= 2 and <= 9 || x is 2 or 6 or 9 && y is >= 1 and <= 4;
            pixels[offset] = glyph ? (byte)245 : b;
            pixels[offset + 1] = glyph ? (byte)245 : g;
            pixels[offset + 2] = glyph ? (byte)245 : r;
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private static CapturedFrame Occlude(CapturedFrame frame, FrameRect area)
    {
        byte[] pixels = frame.BgraPixels.ToArray();
        for (int y = area.Y; y < area.Bottom; y++)
        for (int x = area.X; x < area.Right; x++)
        {
            int offset = y * frame.Stride + x * 4;
            pixels[offset] = 5;
            pixels[offset + 1] = 5;
            pixels[offset + 2] = 5;
            pixels[offset + 3] = 255;
        }
        return frame with { BgraPixels = pixels };
    }
}
