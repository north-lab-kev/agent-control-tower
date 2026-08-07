using Act.App.Desktop;
using Act.App.Notifications;
using Act.App.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

internal sealed class RecordingNotifier : INotifier
{
    public List<DesktopNotification> Shown { get; } = [];

    public Task ShowAsync(DesktopNotification notification)
    {
        Shown.Add(notification);

        return Task.CompletedTask;
    }
}

internal static class TestNotifications
{
    public static NotificationDispatcher Dispatcher(
        UserSettingsService settings,
        INotifier notifier,
        UiPresence? presence = null)
        => new(settings, presence ?? new UiPresence(), notifier, NullLogger<NotificationDispatcher>.Instance);
}
