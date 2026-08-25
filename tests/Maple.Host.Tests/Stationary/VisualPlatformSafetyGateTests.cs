using Maple.Host.Stationary;

namespace Maple.Host.Tests.Stationary;

public sealed class VisualPlatformSafetyGateTests
{
    private static readonly FrameRect Platform = new(100, 300, 400, 80);

    [Theory]
    [InlineData(250, VisualSafetyState.Safe)]
    [InlineData(120, VisualSafetyState.GuardLeft)]
    [InlineData(480, VisualSafetyState.GuardRight)]
    [InlineData(90, VisualSafetyState.Outside)]
    public void Classifies_trusted_positions_against_fixed_platform(double x, VisualSafetyState expected)
    {
        var gate = new VisualPlatformSafetyGate(Platform, 1366);

        VisualPlatformState state = gate.ObserveTrusted(4, x, 0.98);

        Assert.Equal(expected, state.State);
        Assert.Equal(32, state.GuardWidthPx);
    }

    [Fact]
    public void Guard_grows_for_observed_motion_and_never_shrinks()
    {
        var gate = new VisualPlatformSafetyGate(Platform, 1366);

        gate.RecordMovement(200, 250, jitterPx: 4);
        int expanded = gate.GuardWidthPx;
        gate.RecordMovement(250, 252, jitterPx: 0);

        Assert.Equal(62, expanded);
        Assert.Equal(expanded, gate.GuardWidthPx);
    }

    [Fact]
    public void Untrusted_observation_has_no_position_authority()
    {
        var gate = new VisualPlatformSafetyGate(Platform, 1366);

        VisualPlatformState state = gate.ObserveUntrusted(9, 0.72, "VISUAL_NAME_AMBIGUOUS");

        Assert.Equal(VisualSafetyState.Untrusted, state.State);
        Assert.Null(state.CenterX);
    }

    [Fact]
    public void Guard_that_consumes_the_safe_core_freezes_all_movement()
    {
        var gate = new VisualPlatformSafetyGate(Platform, 1366);
        gate.RecordMovement(100, 310, jitterPx: 0);

        VisualPlatformState state = gate.ObserveTrusted(10, 300, 0.98);

        Assert.Equal(VisualSafetyState.Untrusted, state.State);
        Assert.Equal("VISUAL_PLATFORM_GUARD_EXHAUSTED", state.Code);
    }
}
