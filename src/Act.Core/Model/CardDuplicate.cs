namespace Act.Core.Model;

// What survives a copy: the intent — what to run, where, and how. Nothing a run produced does,
// because a duplicate is a task that has not started rather than a fork of a session: no agent
// binding, no badge, no metrics, no history. Lineage is dropped for the same reason — the
// follow-ups a card spawned belong to the run that spawned them, not to its copy.
public static class CardDuplicate
{
    // The title is handed in rather than derived here: naming the copy is a user-facing string,
    // and the core has no resources to localise one with.
    public static Card Of(Card card, string title, DateTimeOffset createdAt) => new()
    {
        Title = title,
        InitialPrompt = card.InitialPrompt,

        // The list is copied here; the files behind it are copied by `BoardState.DuplicateAsync`,
        // because a directory per card is a store concern and the core touches no filesystem.
        Attachments =
        [
            .. card.Attachments.Select(attachment => new TaskAttachment
            {
                FileName = attachment.FileName,
                Length = attachment.Length,
                AddedAt = attachment.AddedAt,
            }),
        ],
        WorkingDir = card.WorkingDir,
        AgentType = card.AgentType,
        LaunchConfig = card.LaunchConfig.Copy(),
        Schedule = card.Schedule,
        ScheduledFor = card.ScheduledFor,
        AutoGit = card.AutoGit is { } git
            ? new AutoGitOptions { Action = git.Action, Draft = git.Draft }
            : null,
        AllowConcurrentWorkingDir = card.AllowConcurrentWorkingDir,
        Column = BoardColumn.Preparing,
        Origin = TaskOrigin.Manual,
        CreatedAt = createdAt,
    };
}
