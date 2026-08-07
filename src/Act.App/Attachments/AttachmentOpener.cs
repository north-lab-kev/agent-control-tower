using Act.App.Desktop;
using Act.Core.Model;
using Act.Infrastructure.FileSystem;

namespace Act.App.Attachments;

// Opening an attachment in whatever the OS associates with it — the only way to look at the ones the
// hover preview cannot show, which is everything that is not an image: a log, a PDF, a spreadsheet.
//
// A named collaborator rather than a method on each page, because both faces that list attachments need
// it and the interesting part is not the click: it is resolving a *name* the card carries into a path
// that really exists, and telling the two failures apart. A file that has gone missing and a file the
// OS has no program for read identically to the user otherwise.
//
// It returns an **outcome, never a sentence** — the page owns the wording, the way `TransitionText` owns
// a transition's. See *Anything stored is a code* in the spec.
public sealed class AttachmentOpener(IAttachmentStore attachments, IDesktopBridge desktop)
{
    public async Task<AttachmentOpenResult> OpenAsync(Guid cardId, TaskAttachment attachment)
    {
        // `ResolveInside` rather than `PathFor`: it proves the name still resolves to a real file inside
        // the card's own folder, so a card whose files were purged under it says so instead of handing
        // the shell a path to nothing.
        if (attachments.ResolveInside(cardId, attachment.FileName) is not { } path)
            return new AttachmentOpenResult(AttachmentOpenOutcome.Missing, null);

        return await desktop.OpenPathAsync(path) is { } error
            ? new AttachmentOpenResult(AttachmentOpenOutcome.Failed, error)
            : new AttachmentOpenResult(AttachmentOpenOutcome.Opened, null);
    }
}

public enum AttachmentOpenOutcome
{
    Opened,
    Missing,
    Failed,
}

public sealed record AttachmentOpenResult(AttachmentOpenOutcome Outcome, string? Detail);
