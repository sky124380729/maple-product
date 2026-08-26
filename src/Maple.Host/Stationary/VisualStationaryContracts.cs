namespace Maple.Host.Stationary;

public readonly record struct FrameRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool IsInside(int frameWidth, int frameHeight) =>
        X >= 0 && Y >= 0 && Width > 0 && Height > 0 && Right <= frameWidth && Bottom <= frameHeight;
}

public enum VisualIdentityKind { NameTemplate, CharacterAppearance }

public sealed record VisualCharacterTemplateBank(
    FrameRect Source,
    int TemplateWidth,
    int TemplateHeight,
    byte[][] TemplatesBgra,
    int MatcherVersion,
    DateTimeOffset? CapturedAtUtc = null);

public sealed record VisualStationaryProfile(
    int SchemaVersion,
    int FrameWidth,
    int FrameHeight,
    FrameRect Platform,
    FrameRect NameSource,
    int NameTemplateWidth,
    int NameTemplateHeight,
    byte[] NameTemplateBgra,
    DateTimeOffset UpdatedAtUtc,
    VisualIdentityKind IdentityKind = VisualIdentityKind.NameTemplate,
    VisualCharacterTemplateBank? CharacterAppearance = null)
{
    public const int SchemaVersionLegacyName = 1;
    public const int SchemaVersionCurrent = 2;
}

public sealed record VisualProfileValidationResult(bool IsValid, string Code)
{
    public static VisualProfileValidationResult Valid() => new(true, "VISUAL_PROFILE_READY");
    public static VisualProfileValidationResult Invalid(string code) => new(false, code);
}

public sealed record VisualProfileSaveResult(bool Success, string Code);
public sealed record VisualProfileLoadResult(VisualStationaryProfile? Profile, string Code);
public sealed record VisualProfileDeleteResult(bool Success, string Code);

public sealed record SelfNameMatch(
    bool HasCandidate,
    string Code,
    long FrameSequence,
    double BestScore,
    double SecondBestScore,
    double CenterX,
    double CenterY,
    double SecondCenterX,
    double SecondCenterY);

public enum SelfIdentityStatus { Acquiring, Trusted, Untrusted }

public sealed record SelfIdentityObservation(
    SelfIdentityStatus Status,
    long FrameSequence,
    double? CenterX,
    double? CenterY,
    double BestScore,
    string Code);

public enum VisualSafetyState { Safe, GuardLeft, GuardRight, Outside, Untrusted }

public sealed record VisualPlatformState(
    VisualSafetyState State,
    long FrameSequence,
    double? CenterX,
    double BestScore,
    int GuardWidthPx,
    int? OffsetFromCenterPx,
    string Code);

public sealed record VisualIdentityCandidate(
    FrameRect Bounds,
    double Score,
    bool IsTrusted);

public sealed record VisualMoveDecision(
    bool ShouldMove,
    Maple.Core.Movement.MovementDirection? Direction,
    int HoldMs,
    string Code)
{
    public static VisualMoveDecision Frozen(string code) => new(false, null, 0, code);
}

public sealed record VisualStationaryObservation(
    long FrameSequence,
    long CapturedAtMonoMs,
    bool IdentityTrusted,
    SelfIdentityStatus IdentityStatus,
    VisualPlatformState Platform,
    string Code,
    VisualIdentityCandidate? IdentityCandidate = null);

public sealed record VisualMovementAuthorization(
    VisualStationaryObservation Observation,
    CancellationToken RevocationToken);

public interface IVisualStationaryObservationSource
{
    VisualStationaryObservation? Latest { get; }
    bool IsLatestFresh(TimeSpan maximumAge);
    bool IsContinuouslyUntrustedFor(TimeSpan duration);
    void BeginMovementTracking(Maple.Core.Movement.MovementDirection direction);
    void EndMovementTracking();
    VisualMovementAuthorization? TryAcquireMovementAuthorization(
        Maple.Core.Movement.MovementDirection direction,
        TimeSpan maximumAge);
    Task<VisualStationaryObservation?> WaitForTrustedAfterAsync(
        long minimumSequence,
        TimeSpan timeout,
        CancellationToken cancellationToken);
    void RecordMovement(double beforeX, double afterX, double jitterPx);
}

public sealed record VisualStartupDecision(bool ShouldStart, string Code);

public static class VisualStationaryStartupPolicy
{
    public static VisualStartupDecision DecideBeforeInput(
        VisualStationaryObservation? observation,
        bool isFresh)
    {
        if (!isFresh) return new VisualStartupDecision(false, "VISUAL_OBSERVATION_STALE");
        return Decide(observation);
    }

    public static VisualStartupDecision Decide(VisualStationaryObservation? observation)
    {
        if (observation is null)
            return new VisualStartupDecision(false, "VISUAL_OBSERVATION_MISSING");
        if (observation.IdentityTrusted)
        {
            return observation.Platform.State == VisualSafetyState.Outside
                ? new VisualStartupDecision(false, observation.Code)
                : new VisualStartupDecision(true, "VISUAL_START_TRUSTED");
        }

        bool temporaryIdentityLoss = IsTemporaryIdentityLossCode(observation.Code);
        return temporaryIdentityLoss
            ? new VisualStartupDecision(true, "VISUAL_START_UNTRUSTED_FROZEN")
            : new VisualStartupDecision(false, observation.Code);
    }

    public static bool IsTemporaryIdentityLossCode(string code) => code is
            "VISUAL_NAME_SCORE_LOW" or
            "VISUAL_NAME_AMBIGUOUS" or
            "VISUAL_NAME_NOT_FOUND" or
            "VISUAL_CHARACTER_NOT_FOUND" or
            "VISUAL_SELF_JUMP" or
            "VISUAL_SELF_ACQUIRING" or
            "VISUAL_SELF_NOT_TRUSTED" or
            "VISUAL_UNTRUSTED_FROZEN";
}

public sealed record VisualStationaryRuntimeState(
    int SchemaVersion,
    Guid SessionId,
    long CycleId,
    string Status,
    long FrameSequence,
    double BestScore,
    int? VisualOffsetPx,
    int GuardWidthPx,
    string Code,
    long UpdatedAtMonoMs,
    string IdentityKind = nameof(VisualIdentityKind.NameTemplate));

public interface IVisualStationaryStatePublisher
{
    void Publish(VisualStationaryRuntimeState state);
}

public static class VisualStationaryProfileValidator
{
    private const double ReferenceFrameWidth = 1366d;
    private const int MaximumNameHeightAtReference = 24;
    private const int MinimumCharacterWidthAtReference = 24;
    private const int MinimumCharacterHeightAtReference = 32;
    private const int MaximumCharacterWidthAtReference = 112;
    private const int MaximumCharacterHeightAtReference = 144;
    private const int CharacterMatcherVersion = 1;
    private const int MaximumCharacterTemplates = 8;
    public const int MinimumPlatformWidth = 96;
    public const int MinimumNameWidth = 8;
    public const int MinimumNameHeight = 4;

    public static VisualProfileValidationResult Validate(
        VisualStationaryProfile profile,
        int currentFrameWidth,
        int currentFrameHeight)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion is not (
            VisualStationaryProfile.SchemaVersionLegacyName or
            VisualStationaryProfile.SchemaVersionCurrent))
            return VisualProfileValidationResult.Invalid("VISUAL_PROFILE_SCHEMA_UNSUPPORTED");
        if (profile.SchemaVersion == VisualStationaryProfile.SchemaVersionLegacyName &&
            (profile.IdentityKind != VisualIdentityKind.NameTemplate || profile.CharacterAppearance is not null))
            return VisualProfileValidationResult.Invalid("VISUAL_PROFILE_SCHEMA_UNSUPPORTED");
        if (!Enum.IsDefined(profile.IdentityKind))
            return VisualProfileValidationResult.Invalid("VISUAL_IDENTITY_KIND_UNSUPPORTED");
        if (profile.FrameWidth <= 0 || profile.FrameHeight <= 0)
            return VisualProfileValidationResult.Invalid("VISUAL_VIEWPORT_INVALID");
        if (profile.FrameWidth != currentFrameWidth || profile.FrameHeight != currentFrameHeight)
            return VisualProfileValidationResult.Invalid("VISUAL_VIEWPORT_MISMATCH");
        if (!profile.Platform.IsInside(profile.FrameWidth, profile.FrameHeight))
            return VisualProfileValidationResult.Invalid("VISUAL_PLATFORM_OUT_OF_FRAME");
        if (profile.Platform.Width < MinimumPlatformWidth)
            return VisualProfileValidationResult.Invalid("VISUAL_PLATFORM_TOO_NARROW");
        return profile.IdentityKind == VisualIdentityKind.CharacterAppearance
            ? ValidateCharacterAppearance(profile)
            : ValidateNameTemplate(profile);
    }

    private static VisualProfileValidationResult ValidateNameTemplate(VisualStationaryProfile profile)
    {
        if (!profile.NameSource.IsInside(profile.FrameWidth, profile.FrameHeight))
            return VisualProfileValidationResult.Invalid("VISUAL_NAME_OUT_OF_FRAME");
        if (profile.NameTemplateWidth < MinimumNameWidth || profile.NameTemplateHeight < MinimumNameHeight)
            return VisualProfileValidationResult.Invalid("VISUAL_NAME_TEMPLATE_TOO_SMALL");
        int maximumNameHeight = Math.Max(
            MinimumNameHeight,
            (int)Math.Ceiling(MaximumNameHeightAtReference * profile.FrameWidth / ReferenceFrameWidth));
        if (profile.NameTemplateHeight > maximumNameHeight)
            return VisualProfileValidationResult.Invalid("VISUAL_NAME_TEMPLATE_TOO_TALL");
        byte[]? pixels = profile.NameTemplateBgra;
        if (pixels is null || pixels.Length != profile.NameTemplateWidth * profile.NameTemplateHeight * 4)
            return VisualProfileValidationResult.Invalid("VISUAL_NAME_TEMPLATE_INVALID");
        if (!HasTexture(pixels))
            return VisualProfileValidationResult.Invalid("VISUAL_NAME_TEMPLATE_LOW_TEXTURE");
        return VisualProfileValidationResult.Valid();
    }

    private static VisualProfileValidationResult ValidateCharacterAppearance(VisualStationaryProfile profile)
    {
        VisualCharacterTemplateBank? bank = profile.CharacterAppearance;
        if (profile.SchemaVersion != VisualStationaryProfile.SchemaVersionCurrent || bank is null)
            return VisualProfileValidationResult.Invalid("VISUAL_CHARACTER_TEMPLATE_INVALID");
        if (!bank.Source.IsInside(profile.FrameWidth, profile.FrameHeight))
            return VisualProfileValidationResult.Invalid("VISUAL_CHARACTER_OUT_OF_FRAME");
        if (bank.Source.Width != bank.TemplateWidth || bank.Source.Height != bank.TemplateHeight)
            return VisualProfileValidationResult.Invalid("VISUAL_CHARACTER_TEMPLATE_INVALID");

        int minimumWidth = Scale(MinimumCharacterWidthAtReference, profile.FrameWidth);
        int minimumHeight = Scale(MinimumCharacterHeightAtReference, profile.FrameWidth);
        int maximumWidth = Scale(MaximumCharacterWidthAtReference, profile.FrameWidth);
        int maximumHeight = Scale(MaximumCharacterHeightAtReference, profile.FrameWidth);
        if (bank.TemplateWidth < minimumWidth || bank.TemplateHeight < minimumHeight)
            return VisualProfileValidationResult.Invalid("VISUAL_CHARACTER_TEMPLATE_TOO_SMALL");
        if (bank.TemplateWidth > maximumWidth || bank.TemplateHeight > maximumHeight)
            return VisualProfileValidationResult.Invalid("VISUAL_CHARACTER_TEMPLATE_TOO_LARGE");
        if (bank.MatcherVersion != CharacterMatcherVersion ||
            bank.TemplatesBgra is null ||
            bank.TemplatesBgra.Length is < 1 or > MaximumCharacterTemplates)
            return VisualProfileValidationResult.Invalid("VISUAL_CHARACTER_TEMPLATE_INVALID");

        int expectedLength = bank.TemplateWidth * bank.TemplateHeight * 4;
        foreach (byte[]? pixels in bank.TemplatesBgra)
        {
            if (pixels is null || pixels.Length != expectedLength)
                return VisualProfileValidationResult.Invalid("VISUAL_CHARACTER_TEMPLATE_INVALID");
            if (!HasTexture(pixels))
                return VisualProfileValidationResult.Invalid("VISUAL_CHARACTER_TEMPLATE_LOW_TEXTURE");
        }
        return VisualProfileValidationResult.Valid();
    }

    private static int Scale(int referencePixels, int frameWidth) =>
        Math.Max(1, (int)Math.Ceiling(referencePixels * frameWidth / ReferenceFrameWidth));

    private static bool HasTexture(ReadOnlySpan<byte> pixels)
    {
        int minimum = 255;
        int maximum = 0;
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            int luminance = (pixels[offset] * 11 + pixels[offset + 1] * 59 + pixels[offset + 2] * 30) / 100;
            minimum = Math.Min(minimum, luminance);
            maximum = Math.Max(maximum, luminance);
        }
        return maximum - minimum >= 16;
    }
}
