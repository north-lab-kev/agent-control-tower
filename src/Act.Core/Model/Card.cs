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

    // When a relative schedule was resolved to a real instant — see `ScheduleArming`. Stored rather
    // than recomputed, because "the next window" has to mean the boundary that was next when the
    // card was armed: recomputed after that boundary passes, it would keep pointing at the one after.
    public DateTimeOffset? EligibleAt { get; set; }

    public AutoGitOptions? AutoGit { get; set; }

    // The card's own exemption from the one-task-per-folder guard — see `WorkingDirConflict`. It is
    // read off the card being *started*, so it says "run this one alongside whatever is already
    // there" and never speaks for the card holding the folder.
    public bool AllowConcurrentWorkingDir { get; set; }

    public TaskOrigin Origin { get; set; }

    public Guid? ParentId { get; set; }

    public IList<Guid> Children { get; set; } = [];

    public SpawnAuthor? SpawnAuthor { get; set; }

    public IList<Guid> DependsOn { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LaunchedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // Soft delete. A deleted card leaves the board but stays in the store, restorable from the
    // archive until it is explicitly purged — so nothing a user removes is lost to a misclick.
    // Distinct from `ArchivedAt` below, which is the retention policy on cards nobody deleted:
    // sharing one field would make "restore" mean two different things.
    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    public DateTimeOffset? ArchivedAt { get; set; }

    public bool IsAutoArchived => ArchivedAt is not null;

    public bool KeepOnBoard { get; set; }

    public bool IsOnBoard => DeletedAt is null && ArchivedAt is null;

    public IList<Transition> Transitions { get; set; } = [];

    public string? ObservedModel { get; set; }

    public CardMetrics? Metrics { get; set; }

    // Every badge that lands a card in Your turn, which is the same thing as saying the ball is in
    // the user's court. The blink is driven from here; the badge itself says which of the five it is.
    public bool NeedsAttention
        => Badge is Model.Badge.NeedsPermission
            or Model.Badge.NeedsAnswer
            or Model.Badge.Error
            or Model.Badge.Killed
            or Model.Badge.ReadyForReview;
}
