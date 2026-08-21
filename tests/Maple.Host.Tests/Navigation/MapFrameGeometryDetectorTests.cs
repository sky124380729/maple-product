using Maple.Host.Navigation;
using Maple.Host.Preview;

namespace Maple.Host.Tests.Navigation;

public sealed class MapFrameGeometryDetectorTests
{
    [Fact]
    public void Detects_normalized_horizontal_platform_runs()
    {
        CapturedFrame frame = Frame(120, 100, (x, y) =>
            y == 60 && x is >= 12 and <= 96 ? Pixel(80, 180, 20) : Pixel(0, 0, 0));

        MapFrameGeometry geometry = MapFrameGeometryDetector.Detect(frame);

        MapPlatformCandidate platform = Assert.Single(geometry.Platforms);
        Assert.InRange(platform.XMin, 0.09, 0.11);
        Assert.InRange(platform.XMax, 0.79, 0.81);
        Assert.InRange(platform.Y, 0.59, 0.61);
    }

    [Fact]
    public void Detects_vertical_ladder_candidates()
    {
        CapturedFrame frame = Frame(120, 100, (x, y) =>
            x == 70 && y is >= 20 and <= 65 ? Pixel(115, 115, 115)
            : y == 65 && x is >= 30 and <= 100 ? Pixel(80, 180, 20)
            : Pixel(0, 0, 0));

        MapFrameGeometry geometry = MapFrameGeometryDetector.Detect(frame);

        MapLadderCandidate ladder = Assert.Single(geometry.Ladders);
        Assert.InRange(ladder.X, 0.57, 0.60);
        Assert.InRange(ladder.YMin, 0.19, 0.21);
        Assert.InRange(ladder.YMax, 0.64, 0.66);
    }

    [Fact]
    public void Ignores_short_single_frame_noise()
    {
        CapturedFrame frame = Frame(120, 100, (x, y) =>
            x is >= 20 and <= 25 && y == 30 ? Pixel(80, 180, 20) : Pixel(0, 0, 0));

        MapFrameGeometry geometry = MapFrameGeometryDetector.Detect(frame);

        Assert.Empty(geometry.Platforms);
        Assert.Empty(geometry.Ladders);
    }

    [Fact]
    public void Ignores_brown_vertical_background_texture_as_a_ladder()
    {
        CapturedFrame frame = Frame(120, 100, (x, y) =>
            x == 70 && y is >= 20 and <= 65 ? Pixel(100, 100, 140)
            : y == 65 && x is >= 30 and <= 100 ? Pixel(80, 180, 20)
            : Pixel(0, 0, 0));

        MapFrameGeometry geometry = MapFrameGeometryDetector.Detect(frame);

        Assert.Empty(geometry.Ladders);
    }

    private static CapturedFrame Frame(int width, int height, Func<int, int, byte[]> pixel)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte[] value = pixel(x, y);
            int offset = (y * width + x) * 4;
            pixels[offset] = value[0];
            pixels[offset + 1] = value[1];
            pixels[offset + 2] = value[2];
            pixels[offset + 3] = 255;
        }
        return new CapturedFrame(width, height, width * 4, pixels, 1, 1);
    }

    private static byte[] Pixel(byte b, byte g, byte r) => [b, g, r];
}
