using System.Drawing;
using System.Windows.Forms;
using Maple.Host.Diagnostics;

namespace Maple.WindowsHost.Diagnostics;

public sealed class WindowsSystemNotificationSink : ISystemNotificationSink, IDisposable
{
    private readonly NotifyIcon notifyIcon = new()
    {
        Icon = SystemIcons.Application,
        Visible = true,
        Text = "Maple Product"
    };

    public Task SendAsync(SystemNotification notification, CancellationToken cancellationToken)
    {
        notifyIcon.ShowBalloonTip(4_000, notification.Title, notification.Body, ToolTipIcon.Warning);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
