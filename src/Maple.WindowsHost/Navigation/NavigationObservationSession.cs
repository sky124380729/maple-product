using System.IO;
using System.Security.Cryptography;
using System.Threading.Channels;
using Maple.Host.Navigation;
using Maple.Host.Preview;
using Maple.Host.Recognition;
using Maple.Host.Windows;
using Maple.WindowsHost.Preview;

namespace Maple.WindowsHost.Navigation;

internal sealed class NavigationObservationSession : INavigationObservationSource, IAsyncDisposable
{
    private readonly string packagePath;
    private readonly string packageHash;
    private readonly MapPackageSnapshot map;
    private readonly WindowsGraphicsCaptureSource capture = new();
    private readonly IRecognitionProvider recognition = RecognitionProviderFactory.Create();
    private readonly IRegionTextRecognizer? mapNameRecognizer = WindowsRegionTextRecognizer.TryCreate();
    private readonly IReadOnlyList<BgraTemplate> templates;
    private readonly MinimapLocalizer localizer = new();
    private readonly NavigationLocalizationGate localizationGate = new();
    private readonly MonsterTemplateMatcher monsterMatcher = new();
    private readonly MapViewportProjection viewportProjection = new();
    private readonly MonsterTargetStabilizer monsterStabilizer = new();
    private readonly Channel<CapturedFrame> frames = Channel.CreateBounded<CapturedFrame>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private readonly object latestGate = new();
    private readonly SemaphoreSlim observationSignal = new(0, 1);
    private readonly CancellationTokenSource cancellation = new();
    private NavigationObservation? latest;
    private Task? worker;
    private long lastRecognitionAt;
    private long lastTemplateAt;
    private long lastHashCheckAt;
    private long lastMapNameCheckAt = long.MinValue;
    private RecognitionAnalysis lastAnalysis = new(HudObservation.Empty, [], [], [], null);
    private readonly MapNameVerificationGate mapNameGate;
    private MapNameVerification mapNameVerification = MapNameVerification.Pending;
    private IReadOnlyList<AuthorizedMonster> lastAuthorized = [];

    public NavigationObservationSession(string packagePath, string packageHash, MapPackageSnapshot map)
    {
        this.packagePath = packagePath;
        this.packageHash = packageHash;
        this.map = map;
        mapNameGate = new MapNameVerificationGate(map.Name);
        templates = MapTemplateDecoder.Decode(packagePath, map);
        capture.FrameArrived += OnFrame;
        capture.Faulted += OnFault;
    }

    public async Task StartAsync(WindowIdentity target, CancellationToken token)
    {
        await capture.StartAsync(target.Hwnd, token);
        worker = Task.Run(ProcessAsync, cancellation.Token);
    }

    public async Task<NavigationObservation?> WaitForNewerAsync(long afterSequence, CancellationToken token)
    {
        while (true)
        {
            lock (latestGate)
                if (latest is not null && latest.Localization.FrameSequence > afterSequence) return latest;
            await observationSignal.WaitAsync(TimeSpan.FromMilliseconds(600), token);
            lock (latestGate)
                if (latest is not null && latest.Localization.FrameSequence > afterSequence) return latest;
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        frames.Writer.TryComplete();
        if (worker is not null)
            try { await worker; } catch (OperationCanceledException) { }
        capture.FrameArrived -= OnFrame;
        capture.Faulted -= OnFault;
        await capture.StopAsync();
        await capture.DisposeAsync();
        if (recognition is IAsyncDisposable asyncRecognition) await asyncRecognition.DisposeAsync();
        observationSignal.Dispose();
        cancellation.Dispose();
    }

    private void OnFrame(CapturedFrame frame) => frames.Writer.TryWrite(frame);

    private void OnFault(PreviewFault fault)
    {
        Publish(new NavigationObservation(
            new NavigationLocalization(long.MaxValue, Environment.TickCount64, false, 0, null, null, fault.Code),
            [], null, false));
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (CapturedFrame frame in frames.Reader.ReadAllAsync(cancellation.Token))
            {
                NavigationLocalization raw = localizer.Observe(
                    frame,
                    map,
                    NavigationTraversal.None);
                raw = await VerifyMapNameAsync(frame, raw);
                NavigationLocalization gated = localizationGate.Update(raw);
                if (!gated.MapMatched)
                {
                    Publish(new NavigationObservation(gated, [], null, true));
                    continue;
                }

                if (frame.CapturedAtMonoMs - lastRecognitionAt >= 150)
                {
                    lastAnalysis = await recognition.AnalyzeAsync(frame, cancellation.Token);
                    lastRecognitionAt = frame.CapturedAtMonoMs;
                }
                bool hashValid = true;
                if (frame.CapturedAtMonoMs - lastHashCheckAt >= 1_000)
                {
                    hashValid = await HashMatchesAsync(cancellation.Token);
                    lastHashCheckAt = frame.CapturedAtMonoMs;
                }

                if (frame.CapturedAtMonoMs - lastTemplateAt >= 150)
                {
                    MapMinimapRect? physicalMinimap = map.MinimapRect is { } logicalMinimap
                        && viewportProjection.TryProject(
                            frame,
                            logicalMinimap,
                            map.MinimapReferenceTopInset,
                            out ProjectedMapViewport projected)
                            ? projected.MinimapRect
                            : null;
                    IReadOnlyList<MonsterCandidate> matches = monsterMatcher.Match(
                        frame,
                        templates,
                        map.Thresholds.MonsterColorCorrelation,
                        physicalMinimap);
                    List<RecognitionTarget> excluded = [.. lastAnalysis.OtherPlayers];
                    if (lastAnalysis.Self is SelfObservation self)
                        excluded.Add(new RecognitionTarget(self.X, self.Y, self.Width, self.Height, "self", self.Confidence));
                    IReadOnlyList<MonsterCandidate> authorized = monsterStabilizer.Update(
                        frame.Sequence,
                        matches,
                        lastAnalysis.Monsters,
                        excluded);
                    lastAuthorized = AuthorizePlatforms(authorized, lastAnalysis.Self, gated.PlatformId);
                    lastTemplateAt = frame.CapturedAtMonoMs;
                }
                Publish(new NavigationObservation(
                    gated,
                    lastAuthorized,
                    lastAnalysis.Self is { } currentSelf ? currentSelf.X + currentSelf.Width / 2 : null,
                    hashValid));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Publish(new NavigationObservation(
                new NavigationLocalization(
                    long.MaxValue,
                    Environment.TickCount64,
                    false,
                    0,
                    null,
                    null,
                    "NAVIGATION_OBSERVATION_FAILED:" + exception.GetType().Name),
                [],
                null,
                false));
        }
    }

    private async Task<NavigationLocalization> VerifyMapNameAsync(
        CapturedFrame frame,
        NavigationLocalization localization)
    {
        if (mapNameRecognizer is null || !localization.MapMatched)
            return localization;
        if (lastMapNameCheckAt == long.MinValue
            || frame.CapturedAtMonoMs - lastMapNameCheckAt >= 500)
        {
            if (!MapNameOcrRegion.TryResolve(frame, out PixelRegion region))
                return localization with { MapMatched = false, FaultCode = "MAP_VIEWPORT_MISMATCH" };
            string observed = await mapNameRecognizer.RecognizeAsync(frame, region, cancellation.Token);
            mapNameVerification = mapNameGate.Update(observed);
            lastMapNameCheckAt = frame.CapturedAtMonoMs;
        }
        return mapNameVerification switch
        {
            MapNameVerification.Verified => localization,
            MapNameVerification.Rejected => localization with
            {
                MapMatched = false,
                FaultCode = "MAP_NAME_MISMATCH"
            },
            _ => localization with
            {
                MapMatched = false,
                FaultCode = "MAP_VALIDATION_PENDING"
            }
        };
    }

    private IReadOnlyList<AuthorizedMonster> AuthorizePlatforms(
        IReadOnlyList<MonsterCandidate> candidates,
        SelfObservation? self,
        int? platformId)
    {
        if (self is null || platformId is null) return [];
        double selfFoot = self.Y + self.Height;
        double selfCenter = self.X + self.Width / 2;
        return candidates
            .Where(candidate => Math.Abs(candidate.Y + candidate.Height - selfFoot) <= map.Thresholds.AttackRangeHeightPixels)
            .Select(candidate => new AuthorizedMonster(
                candidate,
                platformId.Value,
                Math.Abs(candidate.X + candidate.Width / 2 - selfCenter)))
            .ToArray();
    }

    private async Task<bool> HashMatchesAsync(CancellationToken token)
    {
        try
        {
            await using FileStream stream = new(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
            string current = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
            return string.Equals(current, packageHash, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private void Publish(NavigationObservation observation)
    {
        lock (latestGate) latest = observation;
        if (observationSignal.CurrentCount == 0) observationSignal.Release();
    }
}
