using Maple.Host.Diagnostics;

namespace Maple.Host.Tests.Diagnostics;

public sealed class NotificationPolicyTests
{
    [Theory]
    [InlineData("FOCUS_LOST", false)]
    [InlineData("OPERATOR_REQUESTED", false)]
    [InlineData("BROKER_DISCONNECTED", true)]
    [InlineData("KEY_UP_FAILED", true)]
    [InlineData("WINDOW_IDENTITY_CHANGED", true)]
    [InlineData("RUNTIME_EXCEPTION:InvalidOperationException", true)]
    public void Classifies_stop_reasons(string reason, bool expectedNotification)
    {
        Assert.Equal(expectedNotification, NotificationPolicy.ShouldNotify(reason));
    }

    [Fact]
    public async Task Sends_only_one_notification_per_session_and_reason()
    {
        var sink = new RecordingNotificationSink();
        var service = new NotificationService(sink);
        Guid sessionId = Guid.NewGuid();

        await service.NotifyStopAsync(sessionId, "BROKER_DISCONNECTED", CancellationToken.None);
        await service.NotifyStopAsync(sessionId, "BROKER_DISCONNECTED", CancellationToken.None);

        Assert.Single(sink.Notifications);
    }

    private sealed class RecordingNotificationSink : ISystemNotificationSink
    {
        public List<SystemNotification> Notifications { get; } = [];

        public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
}
