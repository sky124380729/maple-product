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
        string levelText = string.Empty;
        string nameText = string.Empty;
        if (frame.Height >= 900)
        {
            // The high-DPI client renders level and name on separate rows. Read
            // them independently so the chat ticker cannot contaminate identity.
            PixelRegion levelRegion = new(
                (int)Math.Round(frame.Width * 0.235), layout.Identity.Y,
                (int)Math.Round(frame.Width * 0.040), layout.Identity.Height);
            PixelRegion nameRegion = new(
                Math.Min(frame.Width - 1, (int)Math.Round(frame.Width * 0.265)),
                layout.Identity.Y,
                Math.Min(frame.Width - (int)Math.Round(frame.Width * 0.265), (int)Math.Round(frame.Width * 0.100)),
                layout.Identity.Height);
            levelText = await ocr.RecognizeAsync(frame, levelRegion, cancellationToken).ConfigureAwait(false);
            nameText = await ocr.RecognizeAsync(frame, nameRegion, cancellationToken).ConfigureAwait(false);
            identityText = $"{identityText} {levelText} {nameText}";
        }
        string hpText = await ocr.RecognizeAsync(frame, layout.HpText, cancellationToken).ConfigureAwait(false);
        string mpText = await ocr.RecognizeAsync(frame, layout.MpText, cancellationToken).ConfigureAwait(false);
        string expText = await ocr.RecognizeAsync(frame, layout.ExpText, cancellationToken).ConfigureAwait(false);
        HudIdentity identity = HudTextParser.ParseIdentity(identityText);
        HudIdentity levelIdentity = HudTextParser.ParseIdentity($"LV. {levelText}");
        string? characterName = HudTextParser.ExtractLatinName(identityText)
            ?? HudTextParser.ExtractLatinName(nameText)
            ?? identity.CharacterName;
        string? job = HudTextParser.ExtractJob(identityText) ?? identity.Job;
        HudResource hp = HudTextParser.ParseResource(hpText);
        HudResource mp = HudTextParser.ParseResource(mpText);
        return new HudObservation(
            characterName, levelIdentity.Level ?? identity.Level, job,
            hp.Current, hp.Maximum, mp.Current, mp.Maximum,
            Ratio(hp.Current, hp.Maximum), Ratio(mp.Current, mp.Maximum),
            HudTextParser.ParseExperience(expText), 0.8);
    }

    private static double? Ratio(int? current, int? maximum) =>
        current is int value && maximum is > 0 ? Math.Clamp(value / (double)maximum.Value, 0, 1) : null;
}
