using System.Collections.Concurrent;
using Act.App.Desktop;
using Act.App.Resources;
using Act.App.Settings;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Notifications;

public sealed class NotificationDispatcher(
    UserSettingsService settings,
    UiPresence presence,
    INotifier notifier,
    ILogger<NotificationDispatcher> log)
{
    private readonly ConcurrentDictionary<Guid, Badge> announced = new();

    public void Notify(Card card)
    {
        if (!NotificationTrigger.Wants(card) || card.Badge is not { } badge)
        {
            announced.TryRemove(card.Id, out _);

            return;
        }

        if (announced.TryGetValue(card.Id, out var last) && last == badge)
            return;

        announced[card.Id] = badge;

        if (!settings.Notifications || presence.BoardIsBeingWatched)
            return;

        _ = ShowAsync(new DesktopNotification(
            card.Id,
            Title(badge),
            Text.Format(Strings.Notify_Body, card.Number, CardTitle.Of(card))));
    }

    private async Task ShowAsync(DesktopNotification notification)
    {
        try
        {
            await notifier.ShowAsync(notification);
        }
        catch (Exception error)
        {
            log.LogError(error, "Could not show a notification for task {TaskId}.", notification.TaskId);
        }
    }

    private static string Title(Badge badge) => badge switch
    {
        Badge.NeedsPermission => Strings.Notify_NeedsPermission,
        Badge.NeedsAnswer => Strings.Notify_NeedsAnswer,
        Badge.Error => Strings.Notify_Error,
        Badge.Killed => Strings.Notify_Killed,
        _ => Strings.Notify_ReadyForReview,
    };
}
