using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class SelfIdentityStabilizerTests
{
    [Fact]
    public void Requires_three_distinct_stable_frames_before_trusting_identity()
    {
        var stabilizer = new SelfIdentityStabilizer();

        SelfIdentityObservation first = stabilizer.Update(Match(1, 100));
        SelfIdentityObservation second = stabilizer.Update(Match(2, 104));
        SelfIdentityObservation third = stabilizer.Update(Match(3, 106));

        Assert.Equal(SelfIdentityStatus.Acquiring, first.Status);
        Assert.Equal(SelfIdentityStatus.Acquiring, second.Status);
        Assert.Equal(SelfIdentityStatus.Trusted, third.Status);
        Assert.Equal(106, third.CenterX);
    }

    [Fact]
    public void A_far_ambiguous_peak_does_not_override_the_existing_local_identity_track()
    {
        var stabilizer = new SelfIdentityStabilizer();
        stabilizer.Update(Match(1, 100));
        stabilizer.Update(Match(2, 102));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 104)).Status);

        SelfIdentityObservation tracked = stabilizer.Update(Match(
            4,
            105,
            second: 0.94,
            secondX: 220,
            secondY: 260));

        Assert.Equal(SelfIdentityStatus.Trusted, tracked.Status);
        Assert.Equal(105, tracked.CenterX);
    }

    [Fact]
    public void Tracking_can_keep_the_local_second_peak_when_a_far_candidate_scores_higher()
    {
        var stabilizer = new SelfIdentityStabilizer();
        stabilizer.Update(Match(1, 100));
        stabilizer.Update(Match(2, 102));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 104)).Status);

        SelfIdentityObservation tracked = stabilizer.Update(Match(
            4,
            220,
            best: 0.99,
            second: 0.96,
            secondX: 106,
            secondY: 200));

        Assert.Equal(SelfIdentityStatus.Trusted, tracked.Status);
        Assert.Equal(106, tracked.CenterX);
        Assert.Equal(0.96, tracked.BestScore);
    }

    [Fact]
    public void Local_ambiguity_repeated_sequence_or_jump_still_revokes_trust()
    {
        var stabilizer = new SelfIdentityStabilizer();
        stabilizer.Update(Match(1, 100));
        stabilizer.Update(Match(2, 102));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 104)).Status);

        Assert.Equal(SelfIdentityStatus.Untrusted, stabilizer.Update(Match(
            4,
            100,
            second: 0.94,
            secondX: 108,
            secondY: 200)).Status);
        Assert.Equal(SelfIdentityStatus.Untrusted, stabilizer.Update(Match(4, 105)).Status);
        Assert.Equal(SelfIdentityStatus.Untrusted, stabilizer.Update(Match(5, 140)).Status);
    }

    [Fact]
    public void Initial_acquisition_still_requires_a_globally_unique_best_peak()
    {
        var stabilizer = new SelfIdentityStabilizer();

        SelfIdentityObservation result = stabilizer.Update(Match(
            1,
            100,
            second: 0.94,
            secondX: 220,
            secondY: 260));

        Assert.Equal(SelfIdentityStatus.Acquiring, result.Status);
        Assert.Equal("VISUAL_NAME_AMBIGUOUS", result.Code);
    }

    [Fact]
    public void Initial_acquisition_rejects_three_stable_candidates_below_point_nine()
    {
        var stabilizer = new SelfIdentityStabilizer();

        SelfIdentityObservation[] results =
        [
            stabilizer.Update(Match(1, 100, best: 0.89)),
            stabilizer.Update(Match(2, 100, best: 0.89)),
            stabilizer.Update(Match(3, 100, best: 0.89))
        ];

        Assert.All(results, result => Assert.Equal(SelfIdentityStatus.Acquiring, result.Status));
        Assert.All(results, result => Assert.Equal("VISUAL_NAME_SCORE_LOW", result.Code));
    }

    [Fact]
    public void Established_track_accepts_a_point_eight_six_candidate_inside_twelve_pixels()
    {
        SelfIdentityStabilizer stabilizer = TrustedAt(100);

        SelfIdentityObservation result = stabilizer.Update(Match(4, 106, best: 0.86));

        Assert.Equal(SelfIdentityStatus.Trusted, result.Status);
        Assert.Equal(106, result.CenterX);
    }

    [Fact]
    public void Transient_score_loss_requires_three_local_point_eight_six_frames_to_restore_trust()
    {
        SelfIdentityStabilizer stabilizer = TrustedAt(100);
        Assert.Equal(SelfIdentityStatus.Untrusted, stabilizer.Update(Match(4, 103, best: 0.85)).Status);

        SelfIdentityObservation first = stabilizer.Update(Match(5, 104, best: 0.86));
        SelfIdentityObservation second = stabilizer.Update(Match(6, 105, best: 0.86));
        SelfIdentityObservation third = stabilizer.Update(Match(7, 106, best: 0.86));

        Assert.Equal(SelfIdentityStatus.Acquiring, first.Status);
        Assert.Equal(SelfIdentityStatus.Acquiring, second.Status);
        Assert.Equal(SelfIdentityStatus.Trusted, third.Status);
    }

    [Fact]
    public void Established_track_rejects_a_point_eight_six_candidate_outside_twelve_pixels()
    {
        SelfIdentityStabilizer stabilizer = TrustedAt(100);

        SelfIdentityObservation result = stabilizer.Update(Match(4, 113, best: 0.86));

        Assert.Equal(SelfIdentityStatus.Untrusted, result.Status);
        Assert.Equal("VISUAL_SELF_JUMP", result.Code);
    }

    [Fact]
    public void Established_character_track_rejects_two_local_peaks_inside_the_tracking_margin()
    {
        var stabilizer = new SelfIdentityStabilizer(
            minimumAcquisitionScore: 0.88,
            minimumTrackingScore: 0.82,
            minimumPeakMargin: 0.06,
            requiredFrames: 3,
            maximumJumpPx: 12,
            minimumTrackingPeakMargin: 0.04);
        stabilizer.Update(Match(1, 100, best: 0.92));
        stabilizer.Update(Match(2, 100, best: 0.92));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 100, best: 0.92)).Status);

        SelfIdentityObservation result = stabilizer.Update(Match(
            4,
            101,
            best: 0.85,
            second: 0.83,
            secondX: 105,
            secondY: 200));

        Assert.Equal(SelfIdentityStatus.Untrusted, result.Status);
        Assert.Equal("VISUAL_NAME_AMBIGUOUS", result.Code);
    }

    [Fact]
    public void Initial_character_acquisition_keeps_the_acquisition_threshold_for_all_three_frames()
    {
        var stabilizer = CharacterStabilizer();

        stabilizer.Update(Match(1, 100, best: 0.92));
        SelfIdentityObservation second = stabilizer.Update(Match(2, 100, best: 0.84));
        SelfIdentityObservation third = stabilizer.Update(Match(3, 100, best: 0.84));

        Assert.Equal(SelfIdentityStatus.Acquiring, second.Status);
        Assert.Equal(SelfIdentityStatus.Acquiring, third.Status);
        Assert.Equal("VISUAL_NAME_SCORE_LOW", third.Code);
    }

    [Fact]
    public void Character_tracking_chooses_the_clear_highest_local_peak_not_the_nearest_peak()
    {
        SelfIdentityStabilizer stabilizer = CharacterStabilizer();
        stabilizer.Update(Match(1, 100, best: 0.92));
        stabilizer.Update(Match(2, 100, best: 0.92));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 100, best: 0.92)).Status);

        SelfIdentityObservation result = stabilizer.Update(Match(
            4,
            110,
            best: 0.90,
            second: 0.85,
            secondX: 101,
            secondY: 200));

        Assert.Equal(SelfIdentityStatus.Trusted, result.Status);
        Assert.Equal(110, result.CenterX);
    }

    [Fact]
    public void Character_recovery_checks_both_sides_against_the_last_trusted_anchor()
    {
        SelfIdentityStabilizer stabilizer = CharacterStabilizer();
        stabilizer.Update(Match(1, 100, best: 0.92));
        stabilizer.Update(Match(2, 100, best: 0.92));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 100, best: 0.92)).Status);
        Assert.Equal(SelfIdentityStatus.Untrusted, stabilizer.Update(Match(4, 100, best: 0.67)).Status);
        Assert.Equal(SelfIdentityStatus.Acquiring, stabilizer.Update(Match(5, 112, best: 0.90)).Status);

        SelfIdentityObservation ambiguous = stabilizer.Update(Match(
            6,
            112,
            best: 0.90,
            second: 0.89,
            secondX: 88,
            secondY: 200));

        Assert.Equal(SelfIdentityStatus.Untrusted, ambiguous.Status);
        Assert.Equal("VISUAL_NAME_AMBIGUOUS", ambiguous.Code);
    }

    [Fact]
    public void Character_track_accepts_point_six_eight_and_recovers_only_after_three_new_frames()
    {
        SelfIdentityStabilizer stabilizer = CharacterStabilizer();
        stabilizer.Update(Match(1, 100, best: 0.92));
        stabilizer.Update(Match(2, 100, best: 0.92));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 100, best: 0.92)).Status);

        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(4, 100, best: 0.68)).Status);
        Assert.Equal(SelfIdentityStatus.Untrusted, stabilizer.Update(Match(5, 100, best: 0.67)).Status);
        Assert.Equal(SelfIdentityStatus.Acquiring, stabilizer.Update(Match(6, 100, best: 0.68)).Status);
        Assert.Equal(SelfIdentityStatus.Acquiring, stabilizer.Update(Match(7, 100, best: 0.68)).Status);
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(8, 100, best: 0.68)).Status);
    }

    [Fact]
    public void Established_character_track_tolerates_the_logged_point_six_nine_two_local_score()
    {
        SelfIdentityStabilizer stabilizer = CharacterStabilizer();
        stabilizer.Update(Match(1, 100, best: 0.92));
        stabilizer.Update(Match(2, 100, best: 0.92));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 100, best: 0.92)).Status);

        SelfIdentityObservation result = stabilizer.Update(Match(4, 100, best: 0.692));

        Assert.Equal(SelfIdentityStatus.Trusted, result.Status);
        Assert.Equal(100, result.CenterX);
    }

    [Fact]
    public void Point_six_nine_two_cannot_acquire_a_new_character_track()
    {
        var stabilizer = new SelfIdentityStabilizer(
            minimumAcquisitionScore: VisualStationaryObservationSession.CharacterAcquisitionScoreThreshold,
            minimumTrackingScore: VisualStationaryObservationSession.CharacterTrackingScoreThreshold,
            minimumPeakMargin: 0.06,
            requiredFrames: 3,
            maximumJumpPx: 12,
            minimumTrackingPeakMargin: 0.04,
            preferHighestLocalScore: true);

        SelfIdentityObservation[] results =
        [
            stabilizer.Update(Match(1, 100, best: 0.692)),
            stabilizer.Update(Match(2, 100, best: 0.692)),
            stabilizer.Update(Match(3, 100, best: 0.692))
        ];

        Assert.All(results, result => Assert.Equal(SelfIdentityStatus.Acquiring, result.Status));
        Assert.All(results, result => Assert.Equal("VISUAL_NAME_SCORE_LOW", result.Code));
    }

    [Fact]
    public void Established_character_relocates_only_after_three_high_confidence_frames()
    {
        SelfIdentityStabilizer stabilizer = CharacterStabilizer();
        stabilizer.Update(Match(1, 100, best: 0.92));
        stabilizer.Update(Match(2, 100, best: 0.92));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, 100, best: 0.92)).Status);

        SelfIdentityObservation first = stabilizer.Update(
            Match(4, 170, best: 0.94),
            allowTrackingAnchorAdvance: true,
            allowRelocation: true);
        SelfIdentityObservation second = stabilizer.Update(
            Match(5, 171, best: 0.94),
            allowTrackingAnchorAdvance: true,
            allowRelocation: true);
        SelfIdentityObservation third = stabilizer.Update(
            Match(6, 170, best: 0.94),
            allowTrackingAnchorAdvance: true,
            allowRelocation: true);

        Assert.Equal(SelfIdentityStatus.Acquiring, first.Status);
        Assert.Equal(SelfIdentityStatus.Acquiring, second.Status);
        Assert.Equal(SelfIdentityStatus.Trusted, third.Status);
        Assert.Equal(170, third.CenterX);
    }

    private static SelfIdentityStabilizer TrustedAt(double x)
    {
        var stabilizer = new SelfIdentityStabilizer();
        stabilizer.Update(Match(1, x));
        stabilizer.Update(Match(2, x));
        Assert.Equal(SelfIdentityStatus.Trusted, stabilizer.Update(Match(3, x)).Status);
        return stabilizer;
    }

    private static SelfIdentityStabilizer CharacterStabilizer() => new(
        minimumAcquisitionScore: 0.88,
        minimumTrackingScore: VisualStationaryObservationSession.CharacterTrackingScoreThreshold,
        minimumPeakMargin: 0.06,
        requiredFrames: 3,
        maximumJumpPx: 12,
        minimumTrackingPeakMargin: 0.04,
        preferHighestLocalScore: true);

    private static SelfNameMatch Match(
        long sequence,
        double x,
        double best = 0.98,
        double second = 0.40,
        double secondX = 300,
        double secondY = 300) =>
        new(true, "VISUAL_NAME_CANDIDATE", sequence, best, second, x, 200, secondX, secondY);
}
