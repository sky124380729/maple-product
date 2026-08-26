namespace Maple.Host.Stationary;

public sealed record VisualProfileEditResult(
    bool Success,
    VisualStationaryProfile? Profile,
    string Code);

public static class VisualStationaryProfileEditor
{
    public static VisualProfileEditResult ReplacePlatform(
        VisualStationaryProfile profile,
        FrameRect platform,
        int frameWidth,
        int frameHeight,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.FrameWidth != frameWidth || profile.FrameHeight != frameHeight)
            return Failed("VISUAL_VIEWPORT_MISMATCH");
        if (profile.IdentityKind != VisualIdentityKind.CharacterAppearance ||
            profile.CharacterAppearance is null)
            return Failed("VISUAL_CHARACTER_TEMPLATE_NOT_CONFIGURED");

        VisualStationaryProfile candidate = profile with
        {
            Platform = platform,
            UpdatedAtUtc = updatedAtUtc
        };
        VisualProfileValidationResult validation = VisualStationaryProfileValidator.Validate(
            candidate,
            frameWidth,
            frameHeight);
        return validation.IsValid
            ? new VisualProfileEditResult(true, candidate, "VISUAL_PLATFORM_SAVED")
            : Failed(validation.Code);
    }

    private static VisualProfileEditResult Failed(string code) => new(false, null, code);
}
