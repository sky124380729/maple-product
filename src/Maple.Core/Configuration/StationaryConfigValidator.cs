namespace Maple.Core.Configuration;

public static class StationaryConfigValidator
{
    public static ConfigValidationResult Validate(StationaryAttackConfig config)
    {
        var errors = new List<ConfigValidationError>();

        if (config.SchemaVersion != StationaryAttackConfig.SchemaVersionCurrent)
            Add("schemaVersion", "SCHEMA_VERSION_UNSUPPORTED");
        if (string.IsNullOrWhiteSpace(config.Source)) Add("source", "SOURCE_REQUIRED");
        if (!StationaryAttackConfig.AllowedAttackKeys.Contains(config.AttackKey))
            Add("attackKey", "ATTACK_KEY_NOT_ALLOWED");
        IReadOnlyList<AttackBand>? attackBands = config.AttackBands;
        if (attackBands is null || attackBands.Count != 4)
            Add("attackBands", "ATTACK_BANDS_REQUIRED");
        if (attackBands is not null && attackBands.Sum(band => band.Weight) != 100)
            Add("attackBands", "ATTACK_WEIGHT_TOTAL");

        foreach (AttackBand band in attackBands ?? [])
        {
            if (band.MinMs <= 0 || band.MaxMs < band.MinMs || band.Weight <= 0)
                Add("attackBands", "ATTACK_BAND_INVALID");
            if (band.MaxMs > StationaryAttackConfig.AttackDurationLimitMs)
                Add("attackBands", "ATTACK_DURATION_LIMIT");
        }

        ValidateRange("moveHold", config.MoveHoldMinMs, config.MoveHoldMaxMs);
        ValidateRange("moveGap", config.MoveGapMinMs, config.MoveGapMaxMs);
        ValidateRange("stabilize", config.StabilizeMinMs, config.StabilizeMaxMs);
        ValidateRange("rest", config.RestMinMs, config.RestMaxMs);
        if (config.MaxLateralMoveMs < config.MoveHoldMinMs)
            Add("maxLateralMoveMs", "MOVE_BUDGET_TOO_SMALL");
        if (config.RestProbabilityPercent is < 0 or > 100)
            Add("restProbabilityPercent", "REST_PROBABILITY_INVALID");
        if (config.AttackTriggerMode == AttackTriggerMode.MonsterInRange)
            Add("attackTriggerMode", "ATTACK_TRIGGER_DISABLED");

        return new ConfigValidationResult(errors);

        void Add(string field, string code) => errors.Add(new ConfigValidationError(field, code));
        void ValidateRange(string field, int minimum, int maximum)
        {
            if (minimum <= 0 || maximum < minimum) Add(field, "RANGE_INVALID");
        }
    }
}
