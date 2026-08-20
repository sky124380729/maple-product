using Maple.Host.Preview;

namespace Maple.Host.Recognition;

public interface IRegionTextRecognizer
{
    Task<string> RecognizeAsync(CapturedFrame frame, PixelRegion region, CancellationToken cancellationToken);
}

public sealed class OcrHudRecognitionProvider(IRegionTextRecognizer ocr) : IRecognitionProvider
{
    private readonly HeuristicHudRecognitionProvider visual = new();
    private HudObservation cached = HudObservation.Empty;
    private long lastOcrAt = long.MinValue;

    public async Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        RecognitionAnalysis visualResult = await visual.AnalyzeAsync(frame, cancellationToken).ConfigureAwait(false);
        if (lastOcrAt == long.MinValue || frame.CapturedAtMonoMs - lastOcrAt >= 500)
        {
            lastOcrAt = frame.CapturedAtMonoMs;
            cached = await ReadTextAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        HudObservation hud = cached with
        {
            HpPercent = Ratio(cached.HpCurrent, cached.HpMax) ?? visualResult.Hud.HpPercent,
            MpPercent = Ratio(cached.MpCurrent, cached.MpMax) ?? visualResult.Hud.MpPercent,
            Confidence = cached.CharacterName is null && cached.HpCurrent is null ? visualResult.Hud.Confidence : 0.8
        };
        return visualResult with { Hud = hud };
    }

    private async Task<HudObservation> ReadTextAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        HudFrameLayout layout = AdaptiveHudLayout.Resolve(frame.Width, frame.Height);
        string identityText = await ocr.RecognizeAsync(frame, layout.Identity, cancellationToken).ConfigureAwait(false);
        string hpText = await ocr.RecognizeAsync(frame, layout.HpText, cancellationToken).ConfigureAwait(false);
        string mpText = await ocr.RecognizeAsync(frame, layout.MpText, cancellationToken).ConfigureAwait(false);
        string expText = await ocr.RecognizeAsync(frame, layout.ExpText, cancellationToken).ConfigureAwait(false);
        HudIdentity identity = HudTextParser.ParseIdentity(identityText);
        HudResource hp = HudTextParser.ParseResource(hpText);
        HudResource mp = HudTextParser.ParseResource(mpText);
        return new HudObservation(
            identity.CharacterName, identity.Level, identity.Job,
            hp.Current, hp.Maximum, mp.Current, mp.Maximum,
            Ratio(hp.Current, hp.Maximum), Ratio(mp.Current, mp.Maximum),
            HudTextParser.ParseExperience(expText), 0.8);
    }

    private static double? Ratio(int? current, int? maximum) =>
        current is int value && maximum is > 0 ? Math.Clamp(value / (double)maximum.Value, 0, 1) : null;
}
