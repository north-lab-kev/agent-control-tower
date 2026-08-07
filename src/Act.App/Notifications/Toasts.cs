using Radzen;

namespace Act.App.Notifications;

// The one spelling of a page toast. Every page used to write the four-property initializer out —
// fifteen copies, each restating the duration — so the shape lives here and a change to it is one
// edit. Radzen's own `Notify` stays available; this is only the house style over it.
public static class Toasts
{
    public const int DefaultDuration = 5000;

    public static void Toast(
        this NotificationService notifications,
        NotificationSeverity severity,
        string summary,
        string? detail = null,
        int duration = DefaultDuration)
        => notifications.Notify(new NotificationMessage
        {
            Severity = severity,
            Summary = summary,
            Detail = detail ?? string.Empty,
            Duration = duration,
        });
}
