using Maple.Host.Windows;

namespace Maple.Host.Recognition;

public enum RecognitionHealth { Disabled, Starting, Running, Stale, Faulted, TargetLost }

public sealed record RecognitionTarget(double X, double Y, double Width, double Height, string Kind, double Confidence);

public sealed record SelfObservation(double X, double Y, double Width, double Height, string? Facing, double Confidence);

public sealed record HudObservation(
    string? CharacterName, int? Level, string? Job,
    int? HpCurrent, int? HpMax, int? MpCurrent, int? MpMax,
    double? HpPercent, double? MpPercent, double? ExpPercent, double Confidence)
{
    public static HudObservation Empty { get; } = new(null, null, null, null, null, null, null, null, null, null, 0);
}

public sealed record RecognitionSnapshot
{
    public string SessionId { get; init; } = string.Empty;
    public WindowIdentity? Target { get; init; }
    public long FrameSequence { get; init; }
    public long CapturedAtMonoMs { get; init; }
    public long PublishedAtMonoMs { get; init; }
    public long FrameAgeMs { get; init; }
    public RecognitionHealth Health { get; init; }
    public string? FaultCode { get; init; }
    public SelfObservation? Self { get; init; }
    public HudObservation Hud { get; init; } = HudObservation.Empty;
    public IReadOnlyList<RecognitionTarget> Monsters { get; init; } = [];
    public IReadOnlyList<RecognitionTarget> Drops { get; init; } = [];
    public IReadOnlyList<RecognitionTarget> OtherPlayers { get; init; } = [];

    public static RecognitionSnapshot Create(
        string sessionId, WindowIdentity? target, long frameSequence,
        long capturedAtMonoMs, long publishedAtMonoMs, HudObservation hud,
        IEnumerable<RecognitionTarget> monsters, IEnumerable<RecognitionTarget> drops,
        IEnumerable<RecognitionTarget> otherPlayers, SelfObservation? self,
        long staleAfterMs = 500)
    {
        long age = Math.Max(0, publishedAtMonoMs - capturedAtMonoMs);
        return new RecognitionSnapshot
        {
            SessionId = sessionId, Target = target, FrameSequence = frameSequence,
            CapturedAtMonoMs = capturedAtMonoMs, PublishedAtMonoMs = publishedAtMonoMs,
            FrameAgeMs = age,
            Health = age > staleAfterMs ? RecognitionHealth.Stale : RecognitionHealth.Running,
            Hud = hud,
            Monsters = monsters.ToArray(), Drops = drops.ToArray(), OtherPlayers = otherPlayers.ToArray(), Self = self
        };
    }

    public RecognitionSnapshot WithHealth(RecognitionHealth health, string? faultCode = null) =>
        this with { Health = health, FaultCode = faultCode };
}
