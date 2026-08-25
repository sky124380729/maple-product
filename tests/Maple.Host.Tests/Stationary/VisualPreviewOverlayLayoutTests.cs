using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualPreviewOverlayLayoutTests
{
    [Fact]
    public void Keeps_saved_and_live_geometry_without_attached_text()
    {
        VisualStationaryProfile profile = Profile();
        var observation = new VisualStationaryObservation(
            3,
            30,
            true,
            SelfIdentityStatus.Trusted,
            new VisualPlatformState(VisualSafetyState.Safe, 3, 90, 0.78, 4, 0, "VISUAL_SAFE"),
            "VISUAL_SAFE",
            new VisualIdentityCandidate(new FrameRect(82, 20, 16, 16), 0.78, true));

        IReadOnlyList<VisualPreviewOverlay> overlays = VisualPreviewOverlayLayout.Create(profile, observation);

        Assert.Null(typeof(VisualPreviewOverlay).GetProperty("Label"));
        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.PlatformBoundary && item.Bounds == profile.Platform);
        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.SafeInterior && item.Bounds == new FrameRect(34, 60, 112, 24));
        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.CharacterTemplate && item.Bounds == profile.CharacterAppearance!.Source);
        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.TrustedIdentity && item.Bounds == new FrameRect(82, 20, 16, 16));
    }

    [Fact]
    public void Shows_an_untrusted_best_match_as_a_candidate_instead_of_a_trusted_identity()
    {
        VisualStationaryProfile profile = Profile();
        var observation = new VisualStationaryObservation(
            1,
            10,
            false,
            SelfIdentityStatus.Acquiring,
            new VisualPlatformState(
                VisualSafetyState.Untrusted,
                1,
                null,
                0.69,
                4,
                null,
                "VISUAL_SELF_ACQUIRING"),
            "VISUAL_SELF_ACQUIRING",
            new VisualIdentityCandidate(new FrameRect(80, 20, 16, 16), 0.69, false));

        IReadOnlyList<VisualPreviewOverlay> overlays = VisualPreviewOverlayLayout.Create(profile, observation);

        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.IdentityCandidate && item.Bounds == new FrameRect(80, 20, 16, 16));
        Assert.DoesNotContain(overlays, item => item.Kind == VisualPreviewOverlayKind.TrustedIdentity);
    }

    private static VisualStationaryProfile Profile() => new(
        VisualStationaryProfile.SchemaVersionCurrent,
        200,
        100,
        new FrameRect(30, 60, 120, 24),
        new FrameRect(0, 0, 0, 0),
        0,
        0,
        [],
        DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
        VisualIdentityKind.CharacterAppearance,
        new VisualCharacterTemplateBank(new FrameRect(80, 20, 16, 16), 16, 16, [new byte[16 * 16 * 4]], 1));
}
