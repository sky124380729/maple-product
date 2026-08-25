namespace Maple.Host.Stationary;

public enum VisualPreviewOverlayKind
{
    PlatformBoundary,
    SafeInterior,
    CharacterTemplate,
    TrustedIdentity,
    IdentityCandidate
}

public sealed record VisualPreviewOverlay(
    VisualPreviewOverlayKind Kind,
    FrameRect Bounds,
    string Label);

public static class VisualPreviewOverlayLayout
{
    public static IReadOnlyList<VisualPreviewOverlay> Create(
        VisualStationaryProfile profile,
        VisualStationaryObservation? observation)
    {
        var overlays = new List<VisualPreviewOverlay>
        {
            new(VisualPreviewOverlayKind.PlatformBoundary, profile.Platform, "平台外边界")
        };

        int guardWidth = observation?.Platform.GuardWidthPx ??
            Math.Max(1, (int)Math.Ceiling(32d * profile.FrameWidth / 1366d));
        if (profile.Platform.Width > guardWidth * 2)
        {
            overlays.Add(new VisualPreviewOverlay(
                VisualPreviewOverlayKind.SafeInterior,
                profile.Platform with
                {
                    X = profile.Platform.X + guardWidth,
                    Width = profile.Platform.Width - guardWidth * 2
                },
                "随机移动安全内区"));
        }

        FrameRect template = profile.IdentityKind == VisualIdentityKind.CharacterAppearance
            ? profile.CharacterAppearance!.Source
            : profile.NameSource;
        overlays.Add(new VisualPreviewOverlay(
            VisualPreviewOverlayKind.CharacterTemplate,
            template,
            "人物模板"));

        if (observation?.IdentityCandidate is { } candidate)
        {
            bool trusted = observation.IdentityTrusted && candidate.IsTrusted;
            overlays.Add(new VisualPreviewOverlay(
                trusted
                    ? VisualPreviewOverlayKind.TrustedIdentity
                    : VisualPreviewOverlayKind.IdentityCandidate,
                candidate.Bounds,
                $"{(trusted ? "实时本人" : "实时候选")} {Percent(candidate.Score)}"));
        }
        return overlays;
    }

    private static string Percent(double score) =>
        $"{Math.Round(Math.Clamp(score, 0, 1) * 100, MidpointRounding.AwayFromZero):0}%";
}
