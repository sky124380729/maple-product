using Maple.Core.Configuration;
using Maple.Core.Session;
using Maple.Core.Triggers;

namespace Maple.Core.Tests.Session;

public sealed class StationarySessionContractTests
{
    [Fact]
    public void Rhythm_state_contains_authoritative_deadline_fields()
    {
        var state = new StationaryRhythmState(
            1,
            Guid.NewGuid(),
            12,
            StationaryPhase.AttackHolding,
            27_438,
            123_456_789,
            123_484_227,
            18_420,
            123_465_807,
            null);

        Assert.Equal(27_438, state.PhaseDeadlineMonoMs - state.PhaseStartedMonoMs);
        Assert.Equal(18_420, state.RemainingMs);
    }

    [Fact]
    public void Monster_trigger_is_explicitly_unavailable_in_phase_one()
    {
        var strategy = new MonsterInRangeTriggerStrategy();

        AttackTriggerDecision decision = strategy.ShouldAttack(ObservationContext.Empty);

        Assert.False(decision.ShouldAttack);
        Assert.Equal("ATTACK_TRIGGER_DISABLED", decision.Code);
    }
}
