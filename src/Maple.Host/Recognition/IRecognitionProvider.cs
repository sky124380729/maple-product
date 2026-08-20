using Maple.Host.Preview;

namespace Maple.Host.Recognition;

public sealed record RecognitionAnalysis(
    HudObservation Hud,
    IReadOnlyList<RecognitionTarget> Monsters,
    IReadOnlyList<RecognitionTarget> Drops,
    IReadOnlyList<RecognitionTarget> OtherPlayers,
    SelfObservation? Self)
{
    public static RecognitionAnalysis Empty { get; } = new(HudObservation.Empty, [], [], [], null);
}

public interface IRecognitionProvider
{
    Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken);
}
