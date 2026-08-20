using Maple.Host.Preview;

namespace Maple.Host.Recognition;

public sealed class HeuristicHudRecognitionProvider : IRecognitionProvider
{
    public Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        double? hp = FindBarRatio(frame, static (r, g, b) => r > 150 && g < 120 && b < 120);
        double? mp = FindBarRatio(frame, static (r, g, b) => b > 150 && r < 120 && g < 180);
        double? exp = FindBarRatio(frame, static (r, g, b) => r > 150 && g > 150 && b < 150);
        var hud = new HudObservation(null, null, null, null, null, null, null, hp, mp, exp, 0.35);
        return Task.FromResult(new RecognitionAnalysis(hud, [], [], [], null));
    }

    private static double? FindBarRatio(CapturedFrame frame, Func<byte, byte, byte, bool> predicate)
    {
        int startY = Math.Max(0, (int)(frame.Height * 0.70));
        int endY = Math.Min(frame.Height, (int)(frame.Height * 0.98));
        int bestCount = 0;
        for (int y = startY; y < endY; y++)
        {
            int count = 0;
            for (int x = 0; x < frame.Width; x++)
            {
                int offset = y * frame.Stride + x * 4;
                if (predicate(frame.BgraPixels.Span[offset + 2], frame.BgraPixels.Span[offset + 1], frame.BgraPixels.Span[offset])) count++;
            }
            bestCount = Math.Max(bestCount, count);
        }
        return bestCount < 5 ? null : Math.Clamp(bestCount / (double)frame.Width, 0, 1);
    }
}
