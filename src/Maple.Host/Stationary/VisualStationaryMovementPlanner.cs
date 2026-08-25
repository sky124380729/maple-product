using Maple.Core.Configuration;
using Maple.Core.Movement;
using Maple.Core.Rhythm;

namespace Maple.Host.Stationary;

public sealed class VisualStationaryMovementPlanner(IRandomSource random)
{
    public MovementDirection? RequiredInwardDirection(VisualPlatformState state)
    {
        if (!state.OffsetFromCenterPx.HasValue) return null;
        if (state.State == VisualSafetyState.GuardLeft) return MovementDirection.Right;
        if (state.State == VisualSafetyState.GuardRight) return MovementDirection.Left;
        if (state.State != VisualSafetyState.Safe ||
            Math.Abs((long)state.OffsetFromCenterPx.Value) <= state.GuardWidthPx)
            return null;
        return state.OffsetFromCenterPx.Value < 0
            ? MovementDirection.Right
            : MovementDirection.Left;
    }

    public VisualMoveDecision Sample(StationaryAttackConfig config, VisualPlatformState state)
    {
        MovementDirection? direction = state.State switch
        {
            VisualSafetyState.Safe => random.NextInclusive(1, 2) == 1
                ? MovementDirection.Left
                : MovementDirection.Right,
            VisualSafetyState.GuardLeft => MovementDirection.Right,
            VisualSafetyState.GuardRight => MovementDirection.Left,
            _ => null
        };
        if (!direction.HasValue) return VisualMoveDecision.Frozen(state.Code);
        int holdMs = random.NextInclusive(config.MoveHoldMinMs, config.MoveHoldMaxMs);
        return new VisualMoveDecision(true, direction, holdMs, "VISUAL_MOVE_AUTHORIZED");
    }

    public VisualMoveDecision Authorize(
        StationaryAttackConfig config,
        VisualPlatformState state,
        MovementDirection requestedDirection)
    {
        bool allowed = state.State switch
        {
            VisualSafetyState.Safe => true,
            VisualSafetyState.GuardLeft => requestedDirection == MovementDirection.Right,
            VisualSafetyState.GuardRight => requestedDirection == MovementDirection.Left,
            _ => false
        };
        if (!allowed) return VisualMoveDecision.Frozen(state.Code);
        int holdMs = random.NextInclusive(config.MoveHoldMinMs, config.MoveHoldMaxMs);
        return new VisualMoveDecision(true, requestedDirection, holdMs, "VISUAL_MOVE_AUTHORIZED");
    }
}
