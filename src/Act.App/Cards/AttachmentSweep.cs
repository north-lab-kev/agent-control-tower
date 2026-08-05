using Act.Infrastructure.FileSystem;

namespace Act.App.Cards;

// The backstop for attachment directories no card claims. Files are written the moment they are
// attached rather than on save — a 25 MB paste must not sit in server memory waiting for a Save
// that may never come, and a file dropped onto a live terminal has no save at all — so a task
// abandoned before its first save leaves a directory behind. `TaskView` clears its own on the
// discard path; this covers the exits that never reach it, which is a crash or a killed process.
//
// One pass at startup, and it can only run there: it is safe precisely because the board has been
// loaded and nothing has been created yet, so "no card owns this" is a fact rather than a race
// against a form that is halfway through its first attachment.
public sealed class AttachmentSweep(
    BoardState board,
    IAttachmentStore attachments,
    ILogger<AttachmentSweep> log)
{
    public void Run()
    {
        var known = board.All.Select(card => card.Id).ToHashSet();
        var orphans = attachments.CardIds().Where(id => !known.Contains(id)).ToList();

        if (orphans.Count == 0)
            return;

        log.LogInformation("Clearing {Count} attachment folder(s) no task claims.", orphans.Count);

        foreach (var orphan in orphans)
            attachments.Clear(orphan);
    }
}
