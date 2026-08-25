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
    FrameRect Bounds);

public static class VisualPreviewOverlayLayout
{
    public static IReadOnlyList<VisualPreviewOverlay> Create(
        VisualStationaryProfile profile,
        VisualStationaryObservation? observation)
    {
        var overlays = new List<VisualPreviewOverlay>
        {
            new(VisualPreviewOverlayKind.PlatformBoundary, profile.Platform)
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
                }));
        }

        FrameRect template = profile.IdentityKind == VisualIdentityKind.CharacterAppearance
            ? profile.CharacterAppearance!.Source
            : profile.NameSource;
        overlays.Add(new VisualPreviewOverlay(
            VisualPreviewOverlayKind.CharacterTemplate,
            template));

        if (observation?.IdentityCandidate is { } candidate)
        {
            bool trusted = observation.IdentityTrusted && candidate.IsTrusted;
            overlays.Add(new VisualPreviewOverlay(
                trusted
                    ? VisualPreviewOverlayKind.TrustedIdentity
                    : VisualPreviewOverlayKind.IdentityCandidate,
                candidate.Bounds));
        }
        return overlays;
    }
}
