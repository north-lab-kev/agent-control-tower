using Act.App.Notifications;
using ElectronNET.API;
using ElectronNET.API.Entities;

namespace Act.App.Desktop;

public sealed class ElectronNotifier(DesktopShell shell, DeepLinkRouter links) : INotifier
{
    private bool? supported;

    public async Task ShowAsync(DesktopNotification notification)
    {
        supported ??= await Electron.Notification.IsSupportedAsync();

        if (supported is not true)
            return;

        Electron.Notification.Show(new NotificationOptions(notification.Title, notification.Body)
        {
            Icon = DesktopShell.IconPath,
            OnClick = () => _ = OpenAsync(notification.TaskId),
        });
    }

    private async Task OpenAsync(Guid taskId)
    {
        await shell.RevealAsync();

        links.Open(taskId);
    }
}
