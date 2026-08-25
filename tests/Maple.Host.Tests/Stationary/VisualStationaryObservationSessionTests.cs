using Maple.Core.Movement;
using Maple.Host.Preview;
using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualStationaryObservationSessionTests
{
    [Fact]
    public void Character_appearance_acquisition_uses_the_documented_point_seven_zero_threshold()
    {
        Assert.Equal(0.70, VisualStationaryObservationSession.CharacterAcquisitionScoreThreshold);
    }

    [Fact]
    public void Character_tracking_threshold_is_seventy_percent()
    {
        Assert.Equal(0.70, VisualStationaryObservationSession.CharacterTrackingScoreThreshold);
    }

    [Fact]
    public void Continuous_untrusted_timer_reaches_fallback_at_fifteen_seconds_and_resets_after_reacquisition()
    {
        long now = 100;
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template), monotonicClock: () => now);
        session.MarkUntrusted("VISUAL_NAME_SCORE_LOW");

        now = 15_099;
        Assert.False(session.IsContinuouslyUntrustedFor(TimeSpan.FromSeconds(15)));
        now = 15_100;
        Assert.True(session.IsContinuouslyUntrustedFor(TimeSpan.FromSeconds(15)));

        session.PushFrame(Frame(template, 1, (70, 20)));
        session.PushFrame(Frame(template, 2, (70, 20)));
        Assert.True(session.IsContinuouslyUntrustedFor(TimeSpan.FromSeconds(15)));
        session.PushFrame(Frame(template, 3, (70, 20)));

        Assert.True(session.Latest!.IdentityTrusted);
        Assert.False(session.IsContinuouslyUntrustedFor(TimeSpan.Zero));
    }

    [Fact]
    public void Capture_loss_continues_visual_unavailable_fallback_timing()
    {
        long now = 100;
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template), monotonicClock: () => now);
        session.MarkUntrusted("VISUAL_NAME_SCORE_LOW");
        now = 15_100;
        Assert.True(session.IsContinuouslyUntrustedFor(TimeSpan.FromSeconds(15)));

        session.MarkUntrusted("PREVIEW_CLOSED");

        Assert.True(session.IsContinuouslyUntrustedFor(TimeSpan.Zero));
    }

    [Fact]
    public void Repeated_old_frame_continues_visual_unavailable_fallback_timing()
    {
        long now = 100;
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template), monotonicClock: () => now);
        session.MarkUntrusted("VISUAL_NAME_SCORE_LOW");
        now = 15_100;
        Assert.True(session.IsContinuouslyUntrustedFor(TimeSpan.FromSeconds(15)));

        session.MarkUntrusted("VISUAL_FRAME_NOT_NEW");

        Assert.True(session.IsContinuouslyUntrustedFor(TimeSpan.Zero));
    }

    [Fact]
    public void Publishes_trusted_state_after_three_unique_frames_and_keeps_the_local_track_when_a_far_copy_appears()
    {
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template));

        session.PushFrame(Frame(template, 1, (70, 20)));
        session.PushFrame(Frame(template, 2, (72, 20)));
        session.PushFrame(Frame(template, 3, (74, 20)));

        Assert.NotNull(session.Latest);
        Assert.True(session.Latest.IdentityTrusted);
        Assert.Equal(VisualSafetyState.Safe, session.Latest.Platform.State);

        session.PushFrame(Frame(template, 4, (74, 20), (120, 20)));

        Assert.True(session.Latest.IdentityTrusted);
        Assert.Equal(VisualSafetyState.Safe, session.Latest.Platform.State);
        Assert.Equal("VISUAL_SAFE", session.Latest.Code);
    }

    [Fact]
    public void Viewport_mismatch_is_frozen_without_matching()
    {
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template));
        CapturedFrame frame = SelfNameTemplateMatcherTests.Frame(200, 80) with { Sequence = 1 };

        session.PushFrame(frame);

        Assert.NotNull(session.Latest);
        Assert.False(session.Latest.IdentityTrusted);
        Assert.Equal("VISUAL_VIEWPORT_MISMATCH", session.Latest.Code);
    }

    [Fact]
    public async Task Waits_for_a_newer_trusted_frame_instead_of_reusing_latest_sequence()
    {
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template));
        session.PushFrame(Frame(template, 1, (70, 20)));
        session.PushFrame(Frame(template, 2, (72, 20)));

        Task<VisualStationaryObservation?> waiting = session.WaitForTrustedAfterAsync(
            2,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        session.PushFrame(Frame(template, 3, (74, 20)));

        VisualStationaryObservation? observed = await waiting;
        Assert.NotNull(observed);
        Assert.Equal(3, observed.FrameSequence);
        Assert.True(observed.IdentityTrusted);
    }

    [Fact]
    public void Capture_fault_revokes_trust_and_requires_three_new_frames_to_reacquire()
    {
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template));
        session.PushFrame(Frame(template, 1, (70, 20)));
        session.PushFrame(Frame(template, 2, (72, 20)));
        session.PushFrame(Frame(template, 3, (74, 20)));
        Assert.True(session.Latest!.IdentityTrusted);
        Assert.NotNull(session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        Assert.NotNull(session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));

        session.MarkUntrusted("PREVIEW_CLOSED");
        Assert.False(session.Latest!.IdentityTrusted);
        Assert.Null(session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        Assert.Null(session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));

        session.PushFrame(Frame(template, 4, (74, 20)));
        session.PushFrame(Frame(template, 5, (74, 20)));
        Assert.False(session.Latest.IdentityTrusted);
        Assert.Null(session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        Assert.Null(session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));
        session.PushFrame(Frame(template, 6, (74, 20)));
        Assert.True(session.Latest.IdentityTrusted);
        Assert.NotNull(session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        Assert.NotNull(session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));
    }

    [Fact]
    public void Entering_left_guard_revokes_only_the_outward_left_authorization()
    {
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template));
        session.PushFrame(Frame(template, 1, (74, 20)));
        session.PushFrame(Frame(template, 2, (74, 20)));
        session.PushFrame(Frame(template, 3, (74, 20)));
        VisualMovementAuthorization left = Assert.IsType<VisualMovementAuthorization>(
            session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        VisualMovementAuthorization right = Assert.IsType<VisualMovementAuthorization>(
            session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));

        session.PushFrame(Frame(template, 4, (64, 20)));
        session.PushFrame(Frame(template, 5, (54, 20)));
        session.PushFrame(Frame(template, 6, (44, 20)));
        session.PushFrame(Frame(template, 7, (34, 20)));
        session.PushFrame(Frame(template, 8, (24, 20)));
        session.PushFrame(Frame(template, 9, (14, 20)));

        Assert.True(
            session.Latest!.Platform.State == VisualSafetyState.GuardLeft,
            $"Expected left guard at {session.Latest.Platform.CenterX}, got {session.Latest.Platform.State}.");
        Assert.True(left.RevocationToken.IsCancellationRequested);
        Assert.False(right.RevocationToken.IsCancellationRequested);
        Assert.Null(session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        Assert.NotNull(session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));
    }

    [Fact]
    public void Entering_right_guard_revokes_only_the_outward_right_authorization()
    {
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template));
        session.PushFrame(Frame(template, 1, (74, 20)));
        session.PushFrame(Frame(template, 2, (74, 20)));
        session.PushFrame(Frame(template, 3, (74, 20)));
        VisualMovementAuthorization left = Assert.IsType<VisualMovementAuthorization>(
            session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        VisualMovementAuthorization right = Assert.IsType<VisualMovementAuthorization>(
            session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));

        session.PushFrame(Frame(template, 4, (84, 20)));
        session.PushFrame(Frame(template, 5, (94, 20)));
        session.PushFrame(Frame(template, 6, (104, 20)));
        session.PushFrame(Frame(template, 7, (114, 20)));
        session.PushFrame(Frame(template, 8, (124, 20)));
        session.PushFrame(Frame(template, 9, (133, 20)));

        Assert.Equal(VisualSafetyState.GuardRight, session.Latest!.Platform.State);
        Assert.False(left.RevocationToken.IsCancellationRequested);
        Assert.True(right.RevocationToken.IsCancellationRequested);
        Assert.NotNull(session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        Assert.Null(session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));
    }

    [Fact]
    public void Expanding_guard_reclassifies_the_latest_position_and_revokes_a_newly_outward_direction()
    {
        byte[] template = SelfNameTemplateMatcherTests.Template(8, 4);
        var session = new VisualStationaryObservationSession(Profile(template));
        session.PushFrame(Frame(template, 1, (22, 20)));
        session.PushFrame(Frame(template, 2, (22, 20)));
        session.PushFrame(Frame(template, 3, (22, 20)));
        Assert.Equal(VisualSafetyState.Safe, session.Latest!.Platform.State);
        VisualMovementAuthorization left = Assert.IsType<VisualMovementAuthorization>(
            session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));

        session.RecordMovement(30, 50, jitterPx: 0);

        Assert.Equal(VisualSafetyState.GuardLeft, session.Latest.Platform.State);
        Assert.True(left.RevocationToken.IsCancellationRequested);
        Assert.Null(session.TryAcquireMovementAuthorization(MovementDirection.Left, TimeSpan.MaxValue));
        Assert.NotNull(session.TryAcquireMovementAuthorization(MovementDirection.Right, TimeSpan.MaxValue));
    }

    [Fact]
    public void Character_profile_acquires_locally_and_a_distant_exact_copy_cannot_take_over()
    {
        byte[] appearance = SelfNameTemplateMatcherTests.Template(16, 16);
        var session = new VisualStationaryObservationSession(CharacterProfile(appearance));

        session.PushFrame(CharacterFrame(appearance, 1, (70, 24)));
        session.PushFrame(CharacterFrame(appearance, 2, (71, 24)));
        session.PushFrame(CharacterFrame(appearance, 3, (71, 24)));

        Assert.True(session.Latest!.IdentityTrusted);
        Assert.Equal(79, session.Latest.Platform.CenterX);

        session.PushFrame(CharacterFrame(appearance, 4, (72, 24), (170, 24)));

        Assert.True(session.Latest.IdentityTrusted);
        Assert.Equal(80, session.Latest.Platform.CenterX);
    }

    [Fact]
    public void Character_profile_publishes_the_actual_candidate_bounds_and_trust_state_for_preview()
    {
        byte[] appearance = SelfNameTemplateMatcherTests.Template(16, 16);
        var session = new VisualStationaryObservationSession(CharacterProfile(appearance));

        session.PushFrame(CharacterFrame(appearance, 1, (70, 24)));

        VisualIdentityCandidate acquiring = Assert.IsType<VisualIdentityCandidate>(
            session.Latest!.IdentityCandidate);
        Assert.Equal(new FrameRect(70, 24, 16, 16), acquiring.Bounds);
        Assert.False(acquiring.IsTrusted);
        Assert.True(acquiring.Score >= VisualStationaryObservationSession.CharacterAcquisitionScoreThreshold);

        session.PushFrame(CharacterFrame(appearance, 2, (70, 24)));
        session.PushFrame(CharacterFrame(appearance, 3, (70, 24)));

        VisualIdentityCandidate trusted = Assert.IsType<VisualIdentityCandidate>(
            session.Latest!.IdentityCandidate);
        Assert.Equal(new FrameRect(70, 24, 16, 16), trusted.Bounds);
        Assert.True(trusted.IsTrusted);
    }

    [Fact]
    public void Character_profile_matches_the_same_appearance_after_facing_is_mirrored()
    {
        byte[] appearance = SelfNameTemplateMatcherTests.Template(16, 16);
        byte[] mirrored = Mirror(appearance, 16, 16);
        var session = new VisualStationaryObservationSession(CharacterProfile(appearance));

        session.PushFrame(CharacterFrame(mirrored, 1, (70, 24)));
        session.PushFrame(CharacterFrame(mirrored, 2, (70, 24)));
        session.PushFrame(CharacterFrame(mirrored, 3, (70, 24)));

        Assert.True(session.Latest!.IdentityTrusted);
        Assert.Equal(78, session.Latest.Platform.CenterX);
    }

    [Fact]
    public void Character_profile_freezes_on_local_loss_and_needs_three_local_frames_to_recover()
    {
        byte[] appearance = SelfNameTemplateMatcherTests.Template(16, 16);
        var session = new VisualStationaryObservationSession(CharacterProfile(appearance));
        session.PushFrame(CharacterFrame(appearance, 1, (70, 24)));
        session.PushFrame(CharacterFrame(appearance, 2, (70, 24)));
        session.PushFrame(CharacterFrame(appearance, 3, (70, 24)));
        Assert.True(session.Latest!.IdentityTrusted);

        session.PushFrame(CharacterFrame(appearance, 4, (170, 24)));
        Assert.False(
            session.Latest.IdentityTrusted,
            $"Unexpected local candidate score {session.Latest.Platform.BestScore:F4} at {session.Latest.Platform.CenterX}");
        Assert.Equal(VisualSafetyState.Untrusted, session.Latest.Platform.State);

        session.PushFrame(CharacterFrame(appearance, 5, (70, 24)));
        session.PushFrame(CharacterFrame(appearance, 6, (70, 24)));
        Assert.False(session.Latest.IdentityTrusted);
        session.PushFrame(CharacterFrame(appearance, 7, (70, 24)));
        Assert.True(session.Latest.IdentityTrusted);
    }

    [Fact]
    public void Character_acquisition_cannot_walk_the_anchor_away_from_the_saved_source()
    {
        byte[] appearance = SelfAppearanceTemplateMatcherTests.Template(0);
        var profile = new VisualStationaryProfile(
            VisualStationaryProfile.SchemaVersionCurrent,
            1366,
            200,
            new FrameRect(40, 100, 240, 40),
            new FrameRect(0, 0, 0, 0),
            0,
            0,
            [],
            DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
            VisualIdentityKind.CharacterAppearance,
            new VisualCharacterTemplateBank(new FrameRect(100, 30, 32, 40), 32, 40, [appearance], 1));
        var session = new VisualStationaryObservationSession(profile);

        session.PushFrame(LargeCharacterFrame(appearance, 1, 112));
        session.PushFrame(LargeCharacterFrame(appearance, 2, 124));
        session.PushFrame(LargeCharacterFrame(appearance, 3, 136));

        Assert.False(session.Latest!.IdentityTrusted);
        Assert.Equal(VisualSafetyState.Untrusted, session.Latest.Platform.State);
    }

    [Fact]
    public void Trusted_character_cannot_walk_the_anchor_while_no_direction_movement_is_active()
    {
        byte[] appearance = SelfAppearanceTemplateMatcherTests.Template(0);
        VisualStationaryProfile profile = LargeCharacterProfile(appearance);
        var session = new VisualStationaryObservationSession(profile);
        session.PushFrame(LargeCharacterFrame(appearance, 1, 100));
        session.PushFrame(LargeCharacterFrame(appearance, 2, 100));
        session.PushFrame(LargeCharacterFrame(appearance, 3, 100));
        Assert.True(session.Latest!.IdentityTrusted);

        session.PushFrame(LargeCharacterFrame(appearance, 4, 112));
        session.PushFrame(LargeCharacterFrame(appearance, 5, 124));
        session.PushFrame(LargeCharacterFrame(appearance, 6, 136));

        Assert.NotNull(session.Latest!.Platform.CenterX);
        Assert.InRange(session.Latest.Platform.CenterX!.Value, 104, 128);
    }

    [Fact]
    public void Direction_movement_window_advances_the_anchor_and_closing_it_freezes_the_new_position()
    {
        byte[] appearance = SelfAppearanceTemplateMatcherTests.Template(0);
        var session = new VisualStationaryObservationSession(LargeCharacterProfile(appearance));
        session.PushFrame(LargeCharacterFrame(appearance, 1, 100));
        session.PushFrame(LargeCharacterFrame(appearance, 2, 100));
        session.PushFrame(LargeCharacterFrame(appearance, 3, 100));

        session.BeginMovementTracking(MovementDirection.Right);
        session.PushFrame(LargeCharacterFrame(appearance, 4, 112));
        session.PushFrame(LargeCharacterFrame(appearance, 5, 124));
        session.PushFrame(LargeCharacterFrame(appearance, 6, 136));
        session.EndMovementTracking();

        Assert.True(session.Latest!.IdentityTrusted);
        Assert.Equal(152, session.Latest.Platform.CenterX);

        session.PushFrame(LargeCharacterFrame(appearance, 7, 148));
        Assert.True(session.Latest.IdentityTrusted);
        session.PushFrame(LargeCharacterFrame(appearance, 8, 160));

        Assert.NotNull(session.Latest.Platform.CenterX);
        Assert.InRange(session.Latest.Platform.CenterX!.Value, 140, 164);
    }

    [Fact]
    public void Character_session_owns_a_snapshot_of_profile_template_pixels()
    {
        byte[] appearance = SelfNameTemplateMatcherTests.Template(16, 16);
        byte[] expected = appearance.ToArray();
        var session = new VisualStationaryObservationSession(CharacterProfile(appearance));
        Array.Clear(appearance);

        session.PushFrame(CharacterFrame(expected, 1, (70, 24)));
        session.PushFrame(CharacterFrame(expected, 2, (70, 24)));
        session.PushFrame(CharacterFrame(expected, 3, (70, 24)));

        Assert.True(session.Latest!.IdentityTrusted);
    }

    private static VisualStationaryProfile Profile(byte[] template) => new(
        VisualStationaryProfile.SchemaVersionCurrent,
        160,
        80,
        new FrameRect(20, 40, 120, 24),
        new FrameRect(70, 20, 8, 4),
        8,
        4,
        template,
        DateTimeOffset.Parse("2026-08-22T00:00:00Z"));

    private static VisualStationaryProfile CharacterProfile(byte[] appearance) => new(
        VisualStationaryProfile.SchemaVersionCurrent,
        220,
        100,
        new FrameRect(30, 64, 160, 24),
        new FrameRect(0, 0, 0, 0),
        0,
        0,
        [],
        DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
        VisualIdentityKind.CharacterAppearance,
        new VisualCharacterTemplateBank(new FrameRect(70, 24, 16, 16), 16, 16, [appearance], 1));

    private static VisualStationaryProfile LargeCharacterProfile(byte[] appearance) => new(
        VisualStationaryProfile.SchemaVersionCurrent,
        1366,
        200,
        new FrameRect(40, 100, 240, 40),
        new FrameRect(0, 0, 0, 0),
        0,
        0,
        [],
        DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
        VisualIdentityKind.CharacterAppearance,
        new VisualCharacterTemplateBank(new FrameRect(100, 30, 32, 40), 32, 40, [appearance], 1));

    private static CapturedFrame Frame(byte[] template, long sequence, params (int X, int Y)[] positions) =>
        SelfNameTemplateMatcherTests.Frame(160, 80, template, 8, 4, positions) with
        {
            Sequence = sequence,
            CapturedAtMonoMs = sequence * 10
        };

    private static CapturedFrame CharacterFrame(
        byte[] template,
        long sequence,
        params (int X, int Y)[] positions) =>
        SelfNameTemplateMatcherTests.Frame(220, 100, template, 16, 16, positions) with
        {
            Sequence = sequence,
            CapturedAtMonoMs = sequence * 10
        };

    private static byte[] Mirror(byte[] source, int width, int height)
    {
        byte[] mirrored = new byte[source.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            source.AsSpan((y * width + x) * 4, 4)
                .CopyTo(mirrored.AsSpan((y * width + width - 1 - x) * 4, 4));
        return mirrored;
    }

    private static CapturedFrame LargeCharacterFrame(byte[] appearance, long sequence, int x)
    {
        const int width = 1366, height = 200, templateWidth = 32, templateHeight = 40;
        byte[] pixels = new byte[width * height * 4];
        for (int offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        for (int row = 0; row < templateHeight; row++)
            appearance.AsSpan(row * templateWidth * 4, templateWidth * 4)
                .CopyTo(pixels.AsSpan(((30 + row) * width + x) * 4, templateWidth * 4));
        return new CapturedFrame(width, height, width * 4, pixels, sequence * 10, sequence);
    }
}
