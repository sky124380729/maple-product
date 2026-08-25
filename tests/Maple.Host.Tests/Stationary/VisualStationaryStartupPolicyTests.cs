using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualStationaryStartupPolicyTests
{
    [Theory]
    [InlineData("VISUAL_NAME_SCORE_LOW")]
    [InlineData("VISUAL_NAME_AMBIGUOUS")]
    [InlineData("VISUAL_CHARACTER_NOT_FOUND")]
    [InlineData("VISUAL_SELF_JUMP")]
    public void Temporary_identity_loss_reuses_the_saved_profile_and_starts_frozen(string code)
    {
        VisualStartupDecision decision = VisualStationaryStartupPolicy.Decide(Observation(code));

        Assert.True(decision.ShouldStart);
        Assert.Equal("VISUAL_START_UNTRUSTED_FROZEN", decision.Code);
    }

    [Theory]
    [InlineData("VISUAL_VIEWPORT_MISMATCH")]
    [InlineData("VISUAL_PROFILE_SCHEMA_UNSUPPORTED")]
    [InlineData("VISUAL_PLATFORM_OUT_OF_FRAME")]
    public void Invalid_saved_profile_still_requires_configuration(string code)
    {
        VisualStartupDecision decision = VisualStationaryStartupPolicy.Decide(Observation(code));

        Assert.False(decision.ShouldStart);
        Assert.Equal(code, decision.Code);
    }

    [Fact]
    public void Trusted_outside_position_does_not_start_blindly()
    {
        var observation = new VisualStationaryObservation(
            4,
            100,
            true,
            SelfIdentityStatus.Trusted,
            new VisualPlatformState(VisualSafetyState.Outside, 4, 10, 0.9, 48, -100, "VISUAL_OUTSIDE_FROZEN"),
            "VISUAL_OUTSIDE_FROZEN");

        VisualStartupDecision decision = VisualStationaryStartupPolicy.Decide(observation);

        Assert.False(decision.ShouldStart);
        Assert.Equal("VISUAL_OUTSIDE_FROZEN", decision.Code);
    }

    [Fact]
    public void Capture_or_observer_failure_does_not_start_without_frames()
    {
        VisualStartupDecision decision = VisualStationaryStartupPolicy.Decide(Observation("VISUAL_OBSERVER_FAILED"));

        Assert.False(decision.ShouldStart);
        Assert.Equal("VISUAL_OBSERVER_FAILED", decision.Code);
    }

    [Fact]
    public void Before_input_recheck_allows_fresh_temporary_loss_but_rejects_stale_or_fatal_state()
    {
        Assert.True(VisualStationaryStartupPolicy.DecideBeforeInput(Observation("VISUAL_NAME_SCORE_LOW"), true).ShouldStart);
        Assert.Equal(
            "VISUAL_OBSERVATION_STALE",
            VisualStationaryStartupPolicy.DecideBeforeInput(Observation("VISUAL_NAME_SCORE_LOW"), false).Code);
        Assert.Equal(
            "VISUAL_OBSERVER_FAILED",
            VisualStationaryStartupPolicy.DecideBeforeInput(Observation("VISUAL_OBSERVER_FAILED"), true).Code);
    }

    private static VisualStationaryObservation Observation(string code) => new(
        3,
        100,
        false,
        SelfIdentityStatus.Untrusted,
        new VisualPlatformState(VisualSafetyState.Untrusted, 3, null, 0.71, 48, null, code),
        code);
}
