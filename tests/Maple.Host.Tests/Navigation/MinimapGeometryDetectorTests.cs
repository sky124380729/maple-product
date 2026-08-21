using Maple.Host.Navigation;
using Maple.Host.Preview;

namespace Maple.Host.Tests.Navigation;

public sealed class MinimapGeometryDetectorTests
{
    [Fact]
    public void Detects_global_platform_and_ladder_from_the_dark_minimap_content()
    {
        CapturedFrame frame = Frame(300, 240, (x, y) =>
        {
            if (x == 223 && y is >= 60 and <= 200)
                return Pixel(190, 80, 50);
            if (x < 220 && y is >= 60 and <= 200)
            {
                if (y is >= 100 and <= 103 && x is >= 20 and <= 180 && x is not (80 or 81 or 140))
                    return Pixel(45, 170, 35);
                if (x is >= 98 and <= 101 && y is >= 72 and <= 101)
                    return Pixel(120, 120, 120);
                if (x is >= 108 and <= 112 && y is >= 176 and <= 180)
                    return Pixel(20, 230, 245);
                return Pixel(20, 20, 20);
            }
            return Pixel(180, 190, 200);
        });

        MapFrameGeometry geometry = MinimapGeometryDetector.Detect(frame);

        MapPlatformCandidate platform = Assert.Single(geometry.Platforms);
        Assert.InRange(platform.XMin, 0.08, 0.10);
        Assert.InRange(platform.XMax, 0.81, 0.83);
        Assert.InRange(platform.Y, 0.27, 0.31);
        MapLadderCandidate ladder = Assert.Single(geometry.Ladders);
        Assert.InRange(ladder.X, 0.44, 0.47);
        Assert.InRange(ladder.YMin, 0.07, 0.10);
        Assert.InRange(ladder.YMax, 0.27, 0.30);
        MinimapObservation observation = MinimapGeometryDetector.Observe(frame);
        Assert.NotNull(observation.Self);
        Assert.InRange(observation.Self!.X, 0.49, 0.51);
        Assert.InRange(observation.Self.Y, 0.83, 0.86);
    }

    [Fact]
    public void Returns_empty_when_the_minimap_content_cannot_be_located()
    {
        CapturedFrame frame = Frame(300, 240, (_, _) => Pixel(180, 190, 200));

        MapFrameGeometry geometry = MinimapGeometryDetector.Detect(frame);

        Assert.Empty(geometry.Platforms);
        Assert.Empty(geometry.Ladders);
    }

    [Fact]
    public void Keeps_the_minimap_coordinate_frame_when_dark_gameplay_is_adjacent()
    {
        CapturedFrame frame = Frame(300, 240, (x, y) =>
        {
            if (x == 223 && y is >= 60 and <= 200)
                return Pixel(190, 80, 50);
            if (x < 220 && y is >= 60 and <= 200)
            {
                if (y is >= 100 and <= 103 && x is >= 20 and <= 180)
                    return Pixel(45, 170, 35);
                if (x is >= 108 and <= 112 && y is >= 176 and <= 180)
                    return Pixel(20, 230, 245);
                return Pixel(20, 20, 20);
            }
            if ((x >= 221 && y is >= 40 and <= 220) || y >= 202)
                return Pixel(10, 10, 10);
            return Pixel(180, 190, 200);
        });

        MinimapObservation observation = MinimapGeometryDetector.Observe(frame);

        MapPlatformCandidate platform = Assert.Single(observation.Geometry.Platforms);
        Assert.InRange(platform.XMax, 0.81, 0.83);
        Assert.NotNull(observation.Self);
        Assert.InRange(observation.Self!.X, 0.49, 0.51);
    }

    [Fact]
    public void Rejects_an_unenclosed_dark_region_instead_of_changing_the_coordinate_frame()
    {
        CapturedFrame frame = Frame(300, 240, (x, y) =>
        {
            if ((x < 220 && y is >= 60 and <= 200) || x >= 220 || y > 200)
            {
                if (y is >= 100 and <= 103 && x is >= 20 and <= 180)
                    return Pixel(45, 170, 35);
                return Pixel(20, 20, 20);
            }
            return Pixel(180, 190, 200);
        });

        MinimapObservation observation = MinimapGeometryDetector.Observe(frame);

        Assert.Empty(observation.Geometry.Platforms);
        Assert.Null(observation.Self);
    }

    [Fact]
    public void Rejects_a_connected_dark_region_that_ends_before_the_scan_boundary()
    {
        CapturedFrame frame = Frame(320, 320, (x, y) =>
        {
            if (x < 251 && y is >= 60 and <= 280)
            {
                if (y is >= 100 and <= 103 && x is >= 20 and <= 180)
                    return Pixel(45, 170, 35);
                return Pixel(20, 20, 20);
            }
            return Pixel(180, 190, 200);
        });

        MinimapObservation observation = MinimapGeometryDetector.Observe(frame);

        Assert.Empty(observation.Geometry.Platforms);
        Assert.Null(observation.Self);
    }

    [Fact]
    public void Uses_the_panel_border_instead_of_a_connected_dark_region_below_the_minimap()
    {
        CapturedFrame frame = Frame(320, 320, (x, y) =>
        {
            if (x == 223 && y is >= 60 and <= 200)
                return Pixel(190, 80, 50);
            if (x < 220 && y is >= 60 and <= 200)
            {
                if (y is >= 100 and <= 103 && x is >= 20 and <= 180)
                    return Pixel(45, 170, 35);
                if (x is >= 108 and <= 112 && y is >= 176 and <= 180)
                    return Pixel(20, 230, 245);
                return Pixel(20, 20, 20);
            }
            if (x < 251 && y is >= 201 and <= 280)
                return Pixel(20, 20, 20);
            return Pixel(180, 190, 200);
        });

        MinimapObservation observation = MinimapGeometryDetector.Observe(frame);

        MapPlatformCandidate platform = Assert.Single(observation.Geometry.Platforms);
        Assert.InRange(platform.XMax, 0.81, 0.83);
        Assert.InRange(platform.Y, 0.27, 0.31);
        Assert.NotNull(observation.Self);
        Assert.InRange(observation.Self!.X, 0.49, 0.51);
    }

    [Fact]
    public void Does_not_treat_light_gameplay_as_a_panel_border_below_the_minimap()
    {
        CapturedFrame frame = Frame(320, 320, (x, y) =>
        {
            if (x == 223 && y is >= 60 and <= 200)
                return Pixel(190, 80, 50);
            if (x < 220 && y is >= 60 and <= 239)
            {
                if (y is >= 100 and <= 103 && x is >= 20 and <= 180)
                    return Pixel(45, 170, 35);
                return Pixel(20, 20, 20);
            }
            return Pixel(180, 190, 200);
        });

        MinimapObservation observation = MinimapGeometryDetector.Observe(frame);

        MapPlatformCandidate platform = Assert.Single(observation.Geometry.Platforms);
        Assert.InRange(platform.XMax, 0.81, 0.83);
        Assert.InRange(platform.Y, 0.27, 0.31);
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
