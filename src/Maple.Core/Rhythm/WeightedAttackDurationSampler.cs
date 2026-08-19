using Maple.Core.Configuration;

namespace Maple.Core.Rhythm;

public sealed record AttackDurationSample(int DurationMs, int BandIndex);

public sealed class WeightedAttackDurationSampler(IRandomSource random)
{
    public AttackDurationSample Sample(IReadOnlyList<AttackBand> bands)
    {
        int roll = random.NextInclusive(1, 100);
        int cumulative = 0;
        for (int index = 0; index < bands.Count; index++)
        {
            AttackBand band = bands[index];
            cumulative += band.Weight;
            if (roll <= cumulative)
                return new AttackDurationSample(random.NextInclusive(band.MinMs, band.MaxMs), index);
        }

        throw new InvalidOperationException("Attack band weights must total 100.");
    }
}
