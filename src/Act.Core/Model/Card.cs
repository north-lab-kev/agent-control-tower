namespace Act.Core.Model;

public sealed class Card
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int Number { get; set; }

    public string? SessionId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string InitialPrompt { get; set; } = string.Empty;

    public BoardColumn Column { get; set; }

    public Badge? Badge { get; set; }

    public AgentType AgentType { get; set; }

    public string WorkingDir { get; set; } = string.Empty;

    public LaunchConfig LaunchConfig { get; set; } = new();

    public TaskSchedule? Schedule { get; set; }

    public DateTimeOffset? ScheduledFor { get; set; }

    public bool AutoComplete { get; set; }

    public AutoGitOptions? AutoGit { get; set; }

    public TaskOrigin Origin { get; set; }

    public Guid? ParentId { get; set; }

    public IList<Guid> Children { get; set; } = [];

    public SpawnAuthor? SpawnAuthor { get; set; }

    public IList<Guid> DependsOn { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LaunchedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public IList<Transition> Transitions { get; set; } = [];

    public string? LastMessage { get; set; }

    public string? ObservedModel { get; set; }

    public CardMetrics? Metrics { get; set; }

    public bool NeedsAttention
        => Badge is Model.Badge.NeedsPermission or Model.Badge.NeedsAnswer or Model.Badge.Error;
}
