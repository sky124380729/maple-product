namespace Maple.Host.Diagnostics;

public sealed record SystemNotification(string Title, string Body, string Reason);

public interface ISystemNotificationSink
{
    Task SendAsync(SystemNotification notification, CancellationToken cancellationToken);
}

public static class NotificationPolicy
{
    public static bool ShouldNotify(string reason) => reason is not "FOCUS_LOST" and not "OPERATOR_REQUESTED";
}

public sealed class NotificationService(ISystemNotificationSink sink)
{
    private readonly HashSet<string> sent = [];
    private readonly object sync = new();

    public async Task NotifyStopAsync(Guid sessionId, string reason, CancellationToken cancellationToken)
    {
        if (!NotificationPolicy.ShouldNotify(reason)) return;
        string key = sessionId.ToString("N") + ":" + reason;
        lock (sync)
        {
            if (!sent.Add(key)) return;
        }

        await sink.SendAsync(
            new SystemNotification("Maple Product 已安全停止", "原因：" + reason, reason),
            cancellationToken);
    }
}
