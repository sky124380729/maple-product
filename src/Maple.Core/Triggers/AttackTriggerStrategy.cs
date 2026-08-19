namespace Maple.Core.Triggers;

public sealed record ObservationContext
{
    public static ObservationContext Empty { get; } = new();
}

public sealed record AttackTriggerDecision(bool ShouldAttack, string Code);

public interface IAttackTriggerStrategy
{
    AttackTriggerDecision ShouldAttack(ObservationContext context);
}

public sealed class AlwaysAttackTriggerStrategy : IAttackTriggerStrategy
{
    public AttackTriggerDecision ShouldAttack(ObservationContext context) => new(true, "ATTACK_ALLOWED");
}

public sealed class MonsterInRangeTriggerStrategy : IAttackTriggerStrategy
{
    public AttackTriggerDecision ShouldAttack(ObservationContext context) => new(false, "ATTACK_TRIGGER_DISABLED");
}
