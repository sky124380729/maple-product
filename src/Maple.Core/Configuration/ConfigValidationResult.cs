namespace Maple.Core.Configuration;

public sealed record ConfigValidationError(string Field, string Code);

public sealed record ConfigValidationResult(IReadOnlyList<ConfigValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
