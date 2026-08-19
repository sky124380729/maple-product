namespace Maple.Core.Rhythm;

public interface IRandomSource
{
    int NextInclusive(int minimum, int maximum);
}

public sealed class SystemRandomSource : IRandomSource
{
    public int NextInclusive(int minimum, int maximum) => Random.Shared.Next(minimum, checked(maximum + 1));
}
