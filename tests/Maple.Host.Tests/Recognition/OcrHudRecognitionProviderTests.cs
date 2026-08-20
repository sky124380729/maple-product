using Maple.Host.Preview;
using Maple.Host.Recognition;

namespace Maple.Host.Tests.Recognition;

public sealed class OcrHudRecognitionProviderTests
{
    [Fact]
    public async Task Combines_ocr_identity_and_numbers_with_visual_percentages()
    {
        var ocr = new OrderedOcr(["LV. 43 猎人 Pink丶Bin", "1586/1586", "914/991", "EXP 90% (0.23%)"]);
        var frame = new CapturedFrame(1366, 768, 1366 * 4, new byte[1366 * 768 * 4], 1000, 1);

        RecognitionAnalysis result = await new OcrHudRecognitionProvider(ocr).AnalyzeAsync(frame, CancellationToken.None);

        Assert.Equal("Pink丶Bin", result.Hud.CharacterName);
        Assert.Equal(43, result.Hud.Level);
        Assert.Equal("猎人", result.Hud.Job);
        Assert.Equal(1586, result.Hud.HpCurrent);
        Assert.Equal(1586, result.Hud.HpMax);
        Assert.Equal(914, result.Hud.MpCurrent);
        Assert.Equal(991, result.Hud.MpMax);
        Assert.Equal(0.23, result.Hud.ExpPercent);
    }

    private sealed class OrderedOcr(IReadOnlyList<string> results) : IRegionTextRecognizer
    {
        private int index;
        public Task<string> RecognizeAsync(CapturedFrame frame, PixelRegion region, CancellationToken cancellationToken) =>
            Task.FromResult(results[index++]);
    }
}
