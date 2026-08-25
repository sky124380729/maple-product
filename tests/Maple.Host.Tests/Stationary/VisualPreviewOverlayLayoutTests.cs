using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualPreviewOverlayLayoutTests
{
    [Fact]
    public void Labels_saved_geometry_and_the_live_trusted_identity_without_color_guessing()
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

        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.PlatformBoundary && item.Label == "平台外边界");
        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.SafeInterior && item.Label == "随机移动安全内区");
        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.CharacterTemplate && item.Label == "人物模板");
        Assert.Contains(overlays, item =>
            item.Kind == VisualPreviewOverlayKind.TrustedIdentity && item.Label == "实时本人 78%");
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
            item.Kind == VisualPreviewOverlayKind.IdentityCandidate && item.Label == "实时候选 69%");
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
