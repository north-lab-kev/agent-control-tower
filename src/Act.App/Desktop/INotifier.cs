namespace Act.App.Desktop;

public interface INotifier
{
    Task ShowAsync(DesktopNotification notification);
}

public sealed record DesktopNotification(Guid TaskId, string Title, string Body);
