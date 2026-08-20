namespace Maple.Core.Configuration;

public enum AttackTriggerMode
{
    Always,
    MonsterInRange
}

public sealed record StationaryAttackConfig
{
    public const int SchemaVersionCurrent = 1;
    public const int AttackDurationLimitMs = 60_000;

    public static IReadOnlySet<string> AllowedAttackKeys { get; } = new HashSet<string>(
        ["Ctrl", "Shift", "Space", "A", "S", "D", "F", "Z", "X", "C", "V"],
        StringComparer.OrdinalIgnoreCase);

    public int SchemaVersion { get; init; } = SchemaVersionCurrent;
    public string Source { get; init; } = "safe-default";
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UnixEpoch;
    // Retained only so existing schema-v1 configuration files remain readable.
    public string TargetExecutablePath { get; init; } = string.Empty;
    public string AttackKey { get; init; } = "Ctrl";
    public IReadOnlyList<AttackBand> AttackBands { get; init; } =
    [
        new(1_000, 10_000, 97),
        new(10_000, 20_000, 1),
        new(20_000, 40_000, 1),
        new(40_000, 60_000, 1)
    ];
    public int MaxLateralMoveMs { get; init; } = 80;
    public int MoveHoldMinMs { get; init; } = 30;
    public int MoveHoldMaxMs { get; init; } = 50;
    public int MoveGapMinMs { get; init; } = 30;
    public int MoveGapMaxMs { get; init; } = 120;
    public int StabilizeMinMs { get; init; } = 80;
    public int StabilizeMaxMs { get; init; } = 150;
    public bool RestEnabled { get; init; } = true;
    public int RestProbabilityPercent { get; init; } = 50;
    public int RestMinMs { get; init; } = 2_000;
    public int RestMaxMs { get; init; } = 5_000;
    public AttackTriggerMode AttackTriggerMode { get; init; } = AttackTriggerMode.Always;
    public bool RecognitionEnabled { get; init; }

    public static StationaryAttackConfig Default { get; } = new();
}
