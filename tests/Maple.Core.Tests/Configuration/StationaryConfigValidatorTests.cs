using Maple.Core.Configuration;

namespace Maple.Core.Tests.Configuration;

public sealed class StationaryConfigValidatorTests
{
    [Fact]
    public void Default_configuration_is_valid()
    {
        ConfigValidationResult result = StationaryConfigValidator.Validate(StationaryAttackConfig.Default);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Rejects_attack_duration_above_product_limit()
    {
        AttackBand[] bands = StationaryAttackConfig.Default.AttackBands.ToArray();
        bands[3] = bands[3] with { MaxMs = 60_001 };
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            AttackBands = bands
        };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_DURATION_LIMIT");
    }

    [Fact]
    public void Rejects_weights_that_do_not_total_one_hundred()
    {
        AttackBand[] bands = StationaryAttackConfig.Default.AttackBands.ToArray();
        bands[3] = bands[3] with { Weight = 24 };
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            AttackBands = bands
        };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_WEIGHT_TOTAL");
    }

    [Fact]
    public void Rejects_null_attack_bands_without_throwing()
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with { AttackBands = null! };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_BANDS_REQUIRED");
    }

    [Fact]
    public void Rejects_any_attack_band_count_other_than_four()
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            AttackBands = [new AttackBand(1_000, 2_000, 100)]
        };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_BANDS_REQUIRED");
    }

    [Theory]
    [InlineData("Alt")]
    [InlineData("F12")]
    [InlineData("")]
    public void Rejects_attack_keys_outside_the_shared_allowlist(string key)
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with { AttackKey = key };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_KEY_NOT_ALLOWED");
    }

    [Fact]
    public void Does_not_require_a_target_executable_path()
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with { TargetExecutablePath = string.Empty };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, error => error.Field == "targetExecutablePath");
    }

    [Fact]
    public void Rejects_disabled_monster_trigger_mode()
    {
        StationaryAttackConfig config = StationaryAttackConfig.Default with
        {
            AttackTriggerMode = AttackTriggerMode.MonsterInRange
        };

        ConfigValidationResult result = StationaryConfigValidator.Validate(config);

        Assert.Contains(result.Errors, error => error.Code == "ATTACK_TRIGGER_DISABLED");
    }
}
