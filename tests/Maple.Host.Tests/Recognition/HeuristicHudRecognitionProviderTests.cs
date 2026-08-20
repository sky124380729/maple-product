using Maple.Host.Preview;
using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class HeuristicHudRecognitionProviderTests
{
    [Fact]
    public async Task Reads_red_and_blue_bar_ratios_from_bottom_status_rows()
    {
        var pixels = new byte[100 * 100 * 4];
        for (int x = 0; x < 30; x++) SetPixel(pixels, 100, x, 85, 30, 20, 220);
        for (int x = 0; x < 60; x++) SetPixel(pixels, 100, x, 90, 220, 30, 20);
        var frame = new CapturedFrame(100, 100, 400, pixels, 10, 1);

        var result = await new HeuristicHudRecognitionProvider().AnalyzeAsync(frame, CancellationToken.None);

        Assert.InRange(result.Hud.HpPercent!.Value, 0.25, 0.35);
        Assert.InRange(result.Hud.MpPercent!.Value, 0.55, 0.65);
    }

    private static void SetPixel(byte[] pixels, int width, int x, int y, byte b, byte g, byte r)
    {
        int offset = (y * width + x) * 4;
        pixels[offset] = b;
        pixels[offset + 1] = g;
        pixels[offset + 2] = r;
        pixels[offset + 3] = 255;
    }
}
