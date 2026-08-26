using Maple.Core.Movement;
using Maple.Host.Preview;

namespace Maple.Host.Stationary;

public sealed class VisualStationaryObservationSession : IVisualStationaryObservationSource
{
    public const double CharacterAcquisitionScoreThreshold = 0.70;
    public const double CharacterTrackingScoreThreshold = 0.68;

    private readonly VisualStationaryProfile profile;
    private readonly SelfNameTemplateMatcher nameMatcher;
    private readonly SelfAppearanceTemplateMatcher appearanceMatcher;
    private readonly SelfIdentityStabilizer stabilizer;
    private readonly VisualPlatformSafetyGate safety;
    private readonly object processingSync = new();
    private readonly object sync = new();
    private readonly Func<long> monotonicClock;
    private readonly List<Waiter> waiters = [];
    private CancellationTokenSource leftMovementAuthorization = CreateCancelledSource();
    private CancellationTokenSource rightMovementAuthorization = CreateCancelledSource();
    private VisualStationaryObservation? latest;
    private long? untrustedSinceMonoMs;
    private double appearanceAnchorX;
    private double appearanceAnchorY;
    private bool movementTrackingActive;
    private bool appearanceTrackEstablished;

    public VisualStationaryObservationSession(
        VisualStationaryProfile profile,
        SelfNameTemplateMatcher? matcher = null,
        SelfIdentityStabilizer? stabilizer = null,
        SelfAppearanceTemplateMatcher? appearanceMatcher = null,
        Func<long>? monotonicClock = null)
    {
        this.profile = FreezeCharacterTemplates(profile);
        nameMatcher = matcher ?? new SelfNameTemplateMatcher();
        this.appearanceMatcher = appearanceMatcher ?? new SelfAppearanceTemplateMatcher();
        this.stabilizer = stabilizer ?? CreateStabilizer(this.profile);
        this.monotonicClock = monotonicClock ?? (() => Environment.TickCount64);
        FrameRect anchor = this.profile.CharacterAppearance?.Source ?? this.profile.NameSource;
        appearanceAnchorX = anchor.X + anchor.Width / 2d;
        appearanceAnchorY = anchor.Y + anchor.Height / 2d;
        safety = new VisualPlatformSafetyGate(this.profile.Platform, this.profile.FrameWidth);
    }

    public event Action<VisualStationaryObservation>? ObservationPublished;

    public VisualStationaryObservation? Latest
    {
        get { lock (sync) return latest; }
    }

    public VisualMovementAuthorization? TryAcquireMovementAuthorization(
        MovementDirection direction,
        TimeSpan maximumAge)
    {
        lock (processingSync)
        {
            VisualStationaryObservation? current = Latest;
            if (current is not { IdentityTrusted: true } ||
                Environment.TickCount64 - current.CapturedAtMonoMs > maximumAge.TotalMilliseconds ||
                !IsDirectionAllowed(current.Platform.State, direction))
                return null;

            CancellationToken token = AuthorizationSource(direction).Token;
            return token.IsCancellationRequested
                ? null
                : new VisualMovementAuthorization(current, token);
        }
    }

    public bool IsLatestFresh(TimeSpan maximumAge)
    {
        lock (sync)
        {
            return latest is not null &&
                Environment.TickCount64 - latest.CapturedAtMonoMs <= maximumAge.TotalMilliseconds;
        }
    }

    public bool IsContinuouslyUntrustedFor(TimeSpan duration)
    {
        lock (sync)
        {
            return untrustedSinceMonoMs.HasValue &&
                monotonicClock() - untrustedSinceMonoMs.Value >= duration.TotalMilliseconds;
        }
    }

    public void BeginMovementTracking(MovementDirection direction)
    {
        lock (processingSync) movementTrackingActive = true;
    }

    public void EndMovementTracking()
    {
        lock (processingSync) movementTrackingActive = false;
    }

    public void MarkUntrusted(string code)
    {
        lock (processingSync)
        {
            VisualStationaryObservation? current = Latest;
            stabilizer.Reset();
            Publish(new VisualStationaryObservation(
                current?.FrameSequence ?? 0,
                current?.CapturedAtMonoMs ?? 0,
                false,
                SelfIdentityStatus.Untrusted,
                safety.ObserveUntrusted(
                    current?.FrameSequence ?? 0,
                    current?.Platform.BestScore ?? 0,
                    code),
                code));
        }
    }

    public void PushFrame(CapturedFrame frame)
    {
        lock (processingSync) PushFrameCore(frame);
    }

    private void PushFrameCore(CapturedFrame frame)
    {
        VisualProfileValidationResult validation = VisualStationaryProfileValidator.Validate(
            profile,
            frame.Width,
            frame.Height);
        if (!validation.IsValid)
        {
            stabilizer.Reset();
            Publish(new VisualStationaryObservation(
                frame.Sequence,
                frame.CapturedAtMonoMs,
                false,
                SelfIdentityStatus.Untrusted,
                safety.ObserveUntrusted(frame.Sequence, 0, validation.Code),
                validation.Code));
            return;
        }

        IdentityMatchResult matchResult = MatchIdentity(frame);
        SelfNameMatch match = matchResult.Match;
        SelfIdentityObservation identity = stabilizer.Update(
            match,
            profile.IdentityKind != VisualIdentityKind.CharacterAppearance ||
                movementTrackingActive || matchResult.IsRelocation,
            matchResult.IsRelocation);
        if (profile.IdentityKind == VisualIdentityKind.CharacterAppearance &&
            identity.Status == SelfIdentityStatus.Trusted &&
            identity.CenterX.HasValue && identity.CenterY.HasValue)
        {
            if (!appearanceTrackEstablished || movementTrackingActive || matchResult.IsRelocation)
            {
                appearanceAnchorX = identity.CenterX.Value;
                appearanceAnchorY = identity.CenterY.Value;
            }
            appearanceTrackEstablished = true;
        }
        bool trusted = identity.Status == SelfIdentityStatus.Trusted && identity.CenterX.HasValue;
        VisualPlatformState platform = trusted
            ? safety.ObserveTrusted(frame.Sequence, identity.CenterX!.Value, identity.BestScore)
            : safety.ObserveUntrusted(frame.Sequence, identity.BestScore, identity.Code);
        Publish(new VisualStationaryObservation(
            frame.Sequence,
            frame.CapturedAtMonoMs,
            trusted,
            identity.Status,
            platform,
            trusted ? platform.Code : identity.Code,
            CreateIdentityCandidate(match, trusted)));
    }

    public async Task<VisualStationaryObservation?> WaitForTrustedAfterAsync(
        long minimumSequence,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Waiter waiter;
        lock (sync)
        {
            if (latest is { IdentityTrusted: true } current && current.FrameSequence > minimumSequence)
                return current;
            waiter = new Waiter(minimumSequence);
            waiters.Add(waiter);
        }
        try
        {
            return await waiter.Completion.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            lock (sync) waiters.Remove(waiter);
        }
    }

    public VisualStationaryObservation? RecordMovement(
        double beforeX,
        double afterX,
        double jitterPx,
        VisualStationaryObservation? trustedAnchor = null)
    {
        lock (processingSync)
        {
            safety.RecordMovement(beforeX, afterX, jitterPx);
            VisualStationaryObservation? current = trustedAnchor ?? Latest;
            if (current is not { IdentityTrusted: true } || !current.Platform.CenterX.HasValue) return null;

            VisualPlatformState platform = safety.ObserveTrusted(
                current.FrameSequence,
                current.Platform.CenterX.Value,
                current.Platform.BestScore);
            VisualStationaryObservation updated = current with { Platform = platform, Code = platform.Code };
            Publish(updated);
            return updated;
        }
    }

    private void Publish(VisualStationaryObservation observation)
    {
        UpdateMovementAuthorizations(observation.Platform.State);
        List<Waiter> ready;
        lock (sync)
        {
            latest = observation;
            if (observation.IdentityTrusted)
                untrustedSinceMonoMs = null;
            else
                untrustedSinceMonoMs ??= monotonicClock();
            ready = observation.IdentityTrusted
                ? waiters.Where(waiter => observation.FrameSequence > waiter.MinimumSequence).ToList()
                : [];
            foreach (Waiter waiter in ready) waiters.Remove(waiter);
        }
        foreach (Waiter waiter in ready) waiter.Completion.TrySetResult(observation);
        ObservationPublished?.Invoke(observation);
    }

    private void UpdateMovementAuthorizations(VisualSafetyState state)
    {
        UpdateMovementAuthorization(ref leftMovementAuthorization, IsDirectionAllowed(state, MovementDirection.Left));
        UpdateMovementAuthorization(ref rightMovementAuthorization, IsDirectionAllowed(state, MovementDirection.Right));
    }

    private static void UpdateMovementAuthorization(
        ref CancellationTokenSource authorization,
        bool authorized)
    {
        if (authorized && authorization.IsCancellationRequested)
        {
            CancellationTokenSource previous = authorization;
            authorization = new CancellationTokenSource();
            previous.Dispose();
        }
        else if (!authorized && !authorization.IsCancellationRequested)
        {
            authorization.Cancel();
        }
    }

    private CancellationTokenSource AuthorizationSource(MovementDirection direction) =>
        direction == MovementDirection.Left
            ? leftMovementAuthorization
            : rightMovementAuthorization;

    private static bool IsDirectionAllowed(VisualSafetyState state, MovementDirection direction) =>
        state == VisualSafetyState.Safe ||
        state == VisualSafetyState.GuardLeft && direction == MovementDirection.Right ||
        state == VisualSafetyState.GuardRight && direction == MovementDirection.Left;

    private static CancellationTokenSource CreateCancelledSource()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return source;
    }

    private static FrameRect CreateSearchArea(VisualStationaryProfile profile)
    {
        int horizontalPad = profile.NameTemplateWidth;
        int verticalPad = Math.Max(12, profile.NameTemplateHeight * 2);
        int left = Math.Max(0, profile.Platform.X - horizontalPad);
        int top = Math.Max(0, profile.NameSource.Y - verticalPad);
        int right = Math.Min(profile.FrameWidth, profile.Platform.Right + horizontalPad);
        int bottom = Math.Min(profile.FrameHeight, profile.NameSource.Bottom + verticalPad);
        return new FrameRect(left, top, right - left, bottom - top);
    }

    private IdentityMatchResult MatchIdentity(CapturedFrame frame)
    {
        if (profile.IdentityKind != VisualIdentityKind.CharacterAppearance)
        {
            return new IdentityMatchResult(
                nameMatcher.Match(
                    frame,
                    profile.NameTemplateBgra,
                    profile.NameTemplateWidth,
                    profile.NameTemplateHeight,
                    CreateSearchArea(profile)),
                false);
        }

        VisualCharacterTemplateBank bank = profile.CharacterAppearance!;
        FrameRect localArea = CreateAppearanceSearchArea(bank);
        SelfNameMatch local = appearanceMatcher.Match(
            frame,
            bank.TemplatesBgra,
            bank.TemplateWidth,
            bank.TemplateHeight,
            localArea);
        double minimumLocalScore = appearanceTrackEstablished
            ? CharacterTrackingScoreThreshold
            : CharacterAcquisitionScoreThreshold;
        double minimumLocalMargin = appearanceTrackEstablished ? 0.04 : 0.06;
        bool localEvidenceInsufficient = local.BestScore < minimumLocalScore ||
            local.BestScore - local.SecondBestScore < minimumLocalMargin;
        bool shouldProbePlatform = localEvidenceInsufficient ||
            IsCandidateAtSearchEdge(local, localArea, bank);
        if (!shouldProbePlatform)
            return new IdentityMatchResult(local, false);

        SelfNameMatch platform = appearanceMatcher.Match(
            frame,
            bank.TemplatesBgra,
            bank.TemplateWidth,
            bank.TemplateHeight,
            CreatePlatformAppearanceSearchArea(bank),
            coarseSampleLimit: 16);
        bool distantBetter = platform.BestScore > local.BestScore &&
            IsOutsideAppearanceAnchor(platform);
        return distantBetter
            ? new IdentityMatchResult(platform, true)
            : new IdentityMatchResult(local, false);
    }

    private static bool IsCandidateAtSearchEdge(
        SelfNameMatch match,
        FrameRect searchArea,
        VisualCharacterTemplateBank bank)
    {
        if (!match.HasCandidate || double.IsNaN(match.CenterX) || double.IsNaN(match.CenterY)) return false;
        double candidateX = match.CenterX - bank.TemplateWidth / 2d;
        double candidateY = match.CenterY - bank.TemplateHeight / 2d;
        int maximumX = searchArea.Right - bank.TemplateWidth;
        int maximumY = searchArea.Bottom - bank.TemplateHeight;
        return candidateX <= searchArea.X + 1 || candidateX >= maximumX - 1 ||
            candidateY <= searchArea.Y + 1 || candidateY >= maximumY - 1;
    }

    private VisualIdentityCandidate? CreateIdentityCandidate(SelfNameMatch match, bool trusted)
    {
        if (!match.HasCandidate ||
            double.IsNaN(match.CenterX) || double.IsNaN(match.CenterY) ||
            double.IsInfinity(match.CenterX) || double.IsInfinity(match.CenterY))
            return null;

        int width = profile.IdentityKind == VisualIdentityKind.CharacterAppearance
            ? profile.CharacterAppearance!.TemplateWidth
            : profile.NameTemplateWidth;
        int height = profile.IdentityKind == VisualIdentityKind.CharacterAppearance
            ? profile.CharacterAppearance!.TemplateHeight
            : profile.NameTemplateHeight;
        var bounds = new FrameRect(
            (int)Math.Round(match.CenterX - width / 2d),
            (int)Math.Round(match.CenterY - height / 2d),
            width,
            height);
        return bounds.IsInside(profile.FrameWidth, profile.FrameHeight)
            ? new VisualIdentityCandidate(bounds, Math.Clamp(match.BestScore, 0, 1), trusted)
            : null;
    }

    private FrameRect CreateAppearanceSearchArea(VisualCharacterTemplateBank bank)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(12d * profile.FrameWidth / 1366d));
        int left = Math.Max(0, (int)Math.Floor(appearanceAnchorX - bank.TemplateWidth / 2d - radius));
        int top = Math.Max(0, (int)Math.Floor(appearanceAnchorY - bank.TemplateHeight / 2d - radius));
        int right = Math.Min(
            profile.FrameWidth,
            (int)Math.Ceiling(appearanceAnchorX + bank.TemplateWidth / 2d + radius));
        int bottom = Math.Min(
            profile.FrameHeight,
            (int)Math.Ceiling(appearanceAnchorY + bank.TemplateHeight / 2d + radius));
        return new FrameRect(left, top, right - left, bottom - top);
    }

    private FrameRect CreatePlatformAppearanceSearchArea(VisualCharacterTemplateBank bank)
    {
        int halfWidth = bank.TemplateWidth / 2;
        int halfHeight = bank.TemplateHeight / 2;
        int left = Math.Max(0, profile.Platform.X - halfWidth);
        int right = Math.Min(profile.FrameWidth, profile.Platform.Right + bank.TemplateWidth - halfWidth);
        int top;
        int bottom;
        if (appearanceAnchorY >= profile.Platform.Y && appearanceAnchorY <= profile.Platform.Bottom)
        {
            top = Math.Max(0, profile.Platform.Y - halfHeight);
            bottom = Math.Min(profile.FrameHeight, profile.Platform.Bottom + bank.TemplateHeight - halfHeight);
        }
        else
        {
            int radius = AppearanceSearchRadius();
            top = Math.Max(0, (int)Math.Floor(appearanceAnchorY - halfHeight - radius));
            bottom = Math.Min(
                profile.FrameHeight,
                (int)Math.Ceiling(appearanceAnchorY + bank.TemplateHeight - halfHeight + radius));
        }
        return new FrameRect(left, top, right - left, bottom - top);
    }

    private bool IsOutsideAppearanceAnchor(SelfNameMatch match)
    {
        if (!match.HasCandidate || double.IsNaN(match.CenterX) || double.IsNaN(match.CenterY)) return false;
        int radius = AppearanceSearchRadius();
        return Math.Abs(match.CenterX - appearanceAnchorX) > radius ||
            Math.Abs(match.CenterY - appearanceAnchorY) > radius;
    }

    private int AppearanceSearchRadius() =>
        Math.Max(1, (int)Math.Ceiling(12d * profile.FrameWidth / 1366d));

    private static SelfIdentityStabilizer CreateStabilizer(VisualStationaryProfile profile)
    {
        if (profile.IdentityKind != VisualIdentityKind.CharacterAppearance)
            return new SelfIdentityStabilizer();
        double maximumJump = Math.Max(1, Math.Ceiling(12d * profile.FrameWidth / 1366d));
        return new SelfIdentityStabilizer(
            minimumAcquisitionScore: CharacterAcquisitionScoreThreshold,
            minimumTrackingScore: CharacterTrackingScoreThreshold,
            minimumPeakMargin: 0.06,
            requiredFrames: 3,
            maximumJumpPx: maximumJump,
            minimumTrackingPeakMargin: 0.04,
            preferHighestLocalScore: true);
    }

    private readonly record struct IdentityMatchResult(SelfNameMatch Match, bool IsRelocation);

    private static VisualStationaryProfile FreezeCharacterTemplates(VisualStationaryProfile profile)
    {
        VisualCharacterTemplateBank? bank = profile.CharacterAppearance;
        return bank is null
            ? profile
            : profile with
            {
                CharacterAppearance = bank with
                {
                    TemplatesBgra = bank.TemplatesBgra
                        .Select(template => template?.ToArray() ?? [])
                        .ToArray()
                }
            };
    }

    private sealed class Waiter(long minimumSequence)
    {
        public long MinimumSequence { get; } = minimumSequence;
        public TaskCompletionSource<VisualStationaryObservation?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
