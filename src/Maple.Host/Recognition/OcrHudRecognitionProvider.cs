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
    private string? pendingIdentity;
    private int pendingIdentityCount;

    public async Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        RecognitionAnalysis visualResult = await visual.AnalyzeAsync(frame, cancellationToken).ConfigureAwait(false);
        if (lastOcrAt == long.MinValue || frame.CapturedAtMonoMs - lastOcrAt >= 500)
        {
            lastOcrAt = frame.CapturedAtMonoMs;
            cached = Stabilize(await ReadTextAsync(frame, cancellationToken).ConfigureAwait(false));
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
        // Both the 1366x768 client and the high-DPI client render level/name
        // in the status row. Reading them separately prevents the chat ticker
        // and the HP label from becoming part of the character identity.
        PixelRegion levelRegion = new(
            (int)(frame.Width * 0.210),
            (int)(frame.Height * 0.952),
            (int)(frame.Width * 0.060),
            Math.Max(1, (int)(frame.Height * 0.045)));
        double nameLeft = 0.255;
        double nameWidth = frame.Height >= 900 ? 0.140 : 0.140;
        double nameTop = frame.Height >= 900 ? 0.982 : 0.972;
        double nameHeight = frame.Height >= 900 ? 0.018 : 0.028;
        PixelRegion nameRegion = new(
            Math.Min(frame.Width - 1, (int)Math.Round(frame.Width * nameLeft)),
            (int)(frame.Height * nameTop),
            Math.Min(frame.Width - (int)Math.Round(frame.Width * nameLeft), (int)Math.Round(frame.Width * nameWidth)),
            Math.Max(1, (int)(frame.Height * nameHeight)));
        string levelText = await ocr.RecognizeAsync(frame, levelRegion, cancellationToken).ConfigureAwait(false);
        string nameText = await ocr.RecognizeAsync(frame, nameRegion, cancellationToken).ConfigureAwait(false);
        identityText = $"{identityText} {levelText} {nameText}";
        string hpText = await ocr.RecognizeAsync(frame, layout.HpText, cancellationToken).ConfigureAwait(false);
        string mpText = await ocr.RecognizeAsync(frame, layout.MpText, cancellationToken).ConfigureAwait(false);
        string expText = await ocr.RecognizeAsync(frame, layout.ExpText, cancellationToken).ConfigureAwait(false);
        HudIdentity identity = HudTextParser.ParseIdentity(identityText);
        HudIdentity levelIdentity = HudTextParser.ParseIdentity($"LV. {levelText}");
        string? characterName = HudTextParser.ExtractLatinName(nameText)
            ?? HudTextParser.ExtractLatinName(identityText)
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

    private HudObservation Stabilize(HudObservation next)
    {
        if (cached == HudObservation.Empty) return next;
        string? nextIdentity = next.CharacterName is null && next.Level is null && next.Job is null
            ? null
            : $"{next.CharacterName}|{next.Level}|{next.Job}";
        string? currentIdentity = cached.CharacterName is null && cached.Level is null && cached.Job is null
            ? null
            : $"{cached.CharacterName}|{cached.Level}|{cached.Job}";
        if (nextIdentity is not null && nextIdentity != currentIdentity)
        {
            if (pendingIdentity == nextIdentity) pendingIdentityCount++;
            else { pendingIdentity = nextIdentity; pendingIdentityCount = 1; }
            if (pendingIdentityCount >= 2)
            {
                cached = cached with { CharacterName = next.CharacterName, Level = next.Level, Job = next.Job };
                pendingIdentity = null;
                pendingIdentityCount = 0;
            }
        }
        else if (nextIdentity == currentIdentity)
        {
            pendingIdentity = null;
            pendingIdentityCount = 0;
        }
        return cached with
        {
            HpCurrent = next.HpCurrent ?? cached.HpCurrent,
            HpMax = next.HpMax ?? cached.HpMax,
            MpCurrent = next.MpCurrent ?? cached.MpCurrent,
            MpMax = next.MpMax ?? cached.MpMax,
            ExpPercent = next.ExpPercent ?? cached.ExpPercent,
            Confidence = next.Confidence > 0 ? next.Confidence : cached.Confidence
        };
    }

    private static double? Ratio(int? current, int? maximum) =>
        current is int value && maximum is > 0 ? Math.Clamp(value / (double)maximum.Value, 0, 1) : null;
}
