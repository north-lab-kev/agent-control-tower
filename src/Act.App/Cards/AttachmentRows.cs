using Act.App.Attachments;
using Act.App.Notifications;
using Act.App.Resources;
using Act.Core.Model;
using Act.Infrastructure.FileSystem;
using Radzen;

namespace Act.App.Cards;

// The attachment row's shared half. The task form and the session rail draw the same rows — the
// icon, the label, the hover thumbnail, and what opening one reports — and the two copies had
// started to drift, so the logic lives here and each page keeps only what is genuinely its own:
// which row is being hovered, and the form's remove button.
public static class AttachmentRows
{
    // The card keeps a *name*; the bytes can go without it — a purge, a hand reaching into the data
    // directory. Asked at render time rather than stored, so a row cannot claim a file that is not
    // there.
    public static bool Missing(IAttachmentStore attachments, Guid cardId, TaskAttachment attachment)
        => attachments.ResolveInside(cardId, attachment.FileName) is null;

    // Only what can actually be shown: a log or a PDF has no thumbnail, and neither does a file
    // that is gone — an empty frame under the cursor reads as a bug rather than as "no preview".
    public static bool Previews(TaskAttachment? previewing, TaskAttachment attachment, bool missing)
        => ReferenceEquals(previewing, attachment) && attachment.IsImage && !missing;

    public static string Icon(TaskAttachment attachment, bool missing) => missing
        ? "broken_image"
        : attachment.IsImage ? "image" : "description";

    // Names the file as well as the action, so it doubles as the tooltip a truncated name needs —
    // and says the file is gone rather than offering to open it when there is nothing there.
    public static string Label(TaskAttachment attachment, bool missing) => missing
        ? Text.Format(Strings.NewTask_Attachments_GoneDetail, attachment.FileName)
        : Text.Format(Strings.NewTask_Attachments_Open, attachment.FileName);

    public static string PreviewUrl(Guid cardId, TaskAttachment attachment)
        => AttachmentEndpointExtensions.UrlFor(cardId, attachment);

    // The only way to look at what the thumbnail cannot show — everything that is not an image —
    // and the outcome owns the wording, so both pages report a missing file the same way.
    public static async Task OpenAsync(
        AttachmentOpener opener,
        NotificationService notifications,
        Guid cardId,
        TaskAttachment attachment)
    {
        var result = await opener.OpenAsync(cardId, attachment);

        if (result.Outcome is AttachmentOpenOutcome.Opened)
            return;

        notifications.Toast(
            NotificationSeverity.Warning,
            result.Outcome is AttachmentOpenOutcome.Missing
                ? Strings.NewTask_Attachments_Gone
                : Strings.NewTask_Attachments_OpenFailed,
            result.Outcome is AttachmentOpenOutcome.Missing
                ? Text.Format(Strings.NewTask_Attachments_GoneDetail, attachment.FileName)
                : result.Detail ?? Text.Format(
                    Strings.NewTask_Attachments_OpenFailedDetail,
                    attachment.FileName),
            duration: 8000);
    }
}
