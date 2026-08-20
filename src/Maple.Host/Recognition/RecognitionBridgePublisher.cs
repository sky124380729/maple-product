namespace Maple.Host.Recognition;

public sealed record RecognitionBridgeMessage(string Type, RecognitionBridgeSnapshot Snapshot);

public sealed record RecognitionBridgeSnapshot(
    string Health,
    long FrameAgeMs,
    string? FaultCode,
    HudObservation Hud);

public sealed class RecognitionBridgePublisher(
    Action<RecognitionBridgeMessage> publish,
    long minimumIntervalMs = 250)
{
    private long lastPublishedAt = long.MinValue;
    private string? lastFingerprint;

    public void TryPublish(RecognitionSnapshot snapshot, long nowMonoMs)
    {
        string fingerprint = $"{snapshot.Health}|{snapshot.FaultCode}|{snapshot.Hud.CharacterName}|{snapshot.Hud.Level}|{snapshot.Hud.Job}|{snapshot.Hud.HpCurrent}/{snapshot.Hud.HpMax}|{snapshot.Hud.MpCurrent}/{snapshot.Hud.MpMax}|{snapshot.Hud.ExpPercent:0.##}";
        if (string.Equals(fingerprint, lastFingerprint, StringComparison.Ordinal)) return;
        if (lastPublishedAt != long.MinValue && nowMonoMs - lastPublishedAt < minimumIntervalMs) return;
        lastPublishedAt = nowMonoMs;
        lastFingerprint = fingerprint;
        publish(new RecognitionBridgeMessage(
            "recognition.snapshot",
            new RecognitionBridgeSnapshot(
                ToWireHealth(snapshot.Health),
                Math.Max(0, nowMonoMs - snapshot.CapturedAtMonoMs),
                snapshot.FaultCode,
                snapshot.Hud)));
    }

    private static string ToWireHealth(RecognitionHealth health) => health switch
    {
        RecognitionHealth.TargetLost => "targetLost",
        _ => char.ToLowerInvariant(health.ToString()[0]) + health.ToString()[1..]
    };
}
