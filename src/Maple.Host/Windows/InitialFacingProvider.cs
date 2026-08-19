using Maple.Core.Movement;

namespace Maple.Host.Windows;

public interface IInitialFacingProvider
{
    Task<InitialFacingResolution> ResolveAsync(
        WindowIdentity target,
        string? selection,
        CancellationToken cancellationToken);
}

public sealed record InitialFacingResolution(
    bool Success,
    string Code,
    MovementDirection? Direction,
    string Source,
    double? Confidence)
{
    public static InitialFacingResolution Resolved(
        MovementDirection direction,
        string source,
        double? confidence = null) =>
        new(true, "INITIAL_FACING_RESOLVED", direction, source, confidence);

    public static InitialFacingResolution Rejected(string code, string source) =>
        new(false, code, null, source, null);
}

public sealed class ManualInitialFacingProvider : IInitialFacingProvider
{
    public Task<InitialFacingResolution> ResolveAsync(
        WindowIdentity target,
        string? selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitialFacingResolution result = selection?.Trim().ToLowerInvariant() switch
        {
            "left" => InitialFacingResolution.Resolved(MovementDirection.Left, "manual"),
            "right" => InitialFacingResolution.Resolved(MovementDirection.Right, "manual"),
            _ => InitialFacingResolution.Rejected("INITIAL_FACING_INVALID", "manual")
        };
        return Task.FromResult(result);
    }
}
