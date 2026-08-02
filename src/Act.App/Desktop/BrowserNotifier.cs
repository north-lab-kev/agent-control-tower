namespace Act.App.Desktop;

public sealed class BrowserNotifier : INotifier
{
    public Task ShowAsync(DesktopNotification notification) => Task.CompletedTask;
}
