namespace Act.App.Desktop;

public interface INotifier
{
    Task ShowAsync(DesktopNotification notification);
}

// `TaskId` is nullable because not every notification is about a card: an update that is ready to
// install has nothing to open. A null id means clicking the toast reveals the window and stops
// there, which is the whole of what such a notification can usefully do.
public sealed record DesktopNotification(Guid? TaskId, string Title, string Body);
