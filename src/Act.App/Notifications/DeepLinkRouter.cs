namespace Act.App.Notifications;

public sealed class DeepLinkRouter
{
    public event Action<Guid>? Requested;

    public void Open(Guid taskId) => Requested?.Invoke(taskId);
}
