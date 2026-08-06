namespace Act.Core.Spawning;

// `list_tasks`, with the truncation stated rather than performed in silence. A board bigger than the
// cap is a board an agent must not assume it has read all of — a quietly short list reads exactly
// like a complete one, and the duplicate it was checking for would be in the part that was dropped.
public sealed record TaskListing(
    IReadOnlyList<TaskSummary> Tasks,
    int Total,
    bool Truncated)
{
    public const int MaxTasks = 200;

    public static TaskListing Of(IReadOnlyList<TaskSummary> all)
        => all.Count <= MaxTasks
            ? new TaskListing(all, all.Count, false)
            : new TaskListing([.. all.Take(MaxTasks)], all.Count, true);
}
