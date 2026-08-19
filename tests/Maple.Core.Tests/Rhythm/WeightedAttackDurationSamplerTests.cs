using Maple.Core.Configuration;
using Maple.Core.Rhythm;

namespace Maple.Core.Tests.Rhythm;

public sealed class WeightedAttackDurationSamplerTests
{
    [Theory]
    [InlineData(1, 1_000, 0)]
    [InlineData(5, 10_000, 0)]
    [InlineData(6, 10_000, 1)]
    [InlineData(15, 20_000, 1)]
    [InlineData(16, 20_000, 2)]
    [InlineData(75, 40_000, 2)]
    [InlineData(76, 40_000, 3)]
    [InlineData(100, 60_000, 3)]
    public void Selects_weight_band_at_inclusive_boundaries(int roll, int duration, int expectedBand)
    {
        var random = new SequenceRandomSource(roll, duration);
        var sampler = new WeightedAttackDurationSampler(random);

        AttackDurationSample sample = sampler.Sample(StationaryAttackConfig.Default.AttackBands);

        Assert.Equal(expectedBand, sample.BandIndex);
        Assert.Equal(duration, sample.DurationMs);
    }

    [Fact]
    public void Preserves_millisecond_precision()
    {
        var sampler = new WeightedAttackDurationSampler(new SequenceRandomSource(16, 27_438));

        AttackDurationSample sample = sampler.Sample(StationaryAttackConfig.Default.AttackBands);

        Assert.Equal(27_438, sample.DurationMs);
    }

    private sealed class SequenceRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> values = new(values);

        public int NextInclusive(int minimum, int maximum)
        {
            int value = values.Dequeue();
            Assert.InRange(value, minimum, maximum);
            return value;
        }
    }
}
