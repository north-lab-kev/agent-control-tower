namespace Act.Core.Model;

// Partial field set — carries only what the board renders today.
// The full model (lineage, transitions, launchConfig, autoGit…) lands in roadmap step 3.
public sealed class Card
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public int Number { get; init; }

    public string Title { get; init; } = string.Empty;

    public BoardColumn Column { get; set; }

    public Badge? Badge { get; set; }

    public string AgentType { get; init; } = "claude-code";

    public string? ObservedModel { get; init; }

    public string WorkingDir { get; init; } = string.Empty;

    public TaskSchedule? Schedule { get; init; }

    public DateTimeOffset? ScheduledFor { get; init; }

    public bool AutoComplete { get; init; }

    public int ChildCount { get; init; }

    public CardMetrics? Metrics { get; init; }

    public bool NeedsAttention
        => Badge is Model.Badge.NeedsPermission or Model.Badge.NeedsAnswer or Model.Badge.Error;
}
