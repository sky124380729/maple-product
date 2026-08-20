using Maple.Host.Preview;

namespace Maple.Host.Recognition;

public sealed class DiagnosticRecognitionProvider : IRecognitionProvider
{
    public Task<RecognitionAnalysis> AnalyzeAsync(CapturedFrame frame, CancellationToken cancellationToken) =>
        Task.FromResult(RecognitionAnalysis.Empty);
}
