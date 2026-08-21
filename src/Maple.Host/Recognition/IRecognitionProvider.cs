using Maple.Host.Preview;
using Maple.Host.Navigation;

namespace Maple.Host.Recognition;

public sealed record RecognitionAnalysis(
    HudObservation Hud,
    IReadOnlyList<RecognitionTarget> Monsters,
    IReadOnlyList<RecognitionTarget> Drops,
    IReadOnlyList<RecognitionTarget> OtherPlayers,
    SelfObservation? Self,
    MapFrameGeometry? Geometry = null)
{
    public static RecognitionAnalysis Empty { get; } = new(HudObservation.Empty, [], [], [], null);
}

public interface IRecognitionProvider
{
    Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken);
}
