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
        var cards = board.All;

        // **A board with no cards buys nothing and risks everything.** It is the one state this cannot
        // tell apart from a board that failed to arrive — a store another instance holds, a load that
        // threw somewhere upstream — and being wrong here deletes the user's files rather than some
        // bytes nobody wants. A store with genuinely no cards has nothing to reclaim either, so
        // refusing to act in that case costs precisely zero.
        if (cards.Count == 0)
        {
            log.LogDebug("No cards are loaded, so nothing is swept: an empty board proves nothing.");

            return;
        }

        var claimed = cards.Select(card => card.Id).ToHashSet();
        var orphans = attachments.CardIds().Where(id => !claimed.Contains(id)).ToList();

        if (orphans.Count == 0)
            return;

        // The ids, not only a count. This is the one routine that deletes a user's files without being
        // asked, and "Clearing 5 attachment folder(s)" is exactly as much as you know afterwards when
        // it turns out to have cleared the wrong five — which is not enough to tell whether it did.
        log.LogInformation(
            "Clearing {Count} attachment folder(s) no task claims, against {Cards} loaded card(s): {Folders}",
            orphans.Count,
            cards.Count,
            string.Join(", ", orphans));

        foreach (var orphan in orphans)
            attachments.Clear(orphan);
    }
}
