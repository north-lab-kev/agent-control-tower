using Act.Core.Agents;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Cards;

// A record rather than a class for one reason: value equality. It is what lets the task page ask
// "has anything changed?" by comparing the live form with the snapshot taken when it loaded,
// instead of maintaining a dirty flag per field and forgetting one.
public sealed record NewTaskForm
{
    public string Title { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string WorkingDir { get; set; } = string.Empty;

    public AgentType Agent { get; set; }

    public string? Model { get; set; }

    public string? Effort { get; set; }

    public PermissionMode Permission { get; set; }

    public TaskSchedule Schedule { get; set; } = TaskSchedule.Manual;

    public DateTime? ScheduledFor { get; set; }

    public GitAction? SelectedGitAction { get; set; }

    public bool Draft { get; set; }

    public bool AllowConcurrentWorkingDir { get; set; }

    public bool WantsDateTime => Schedule is TaskSchedule.SpecificDateTime;

    // A new task starts as the settings template — everything but the title and the prompt, which
    // are the task itself and must be typed.
    public static NewTaskForm From(TaskDefaults defaults) => new()
    {
        WorkingDir = defaults.WorkingDir,
        Agent = defaults.Agent,
        Model = defaults.Model,
        Effort = defaults.Effort,
        Permission = defaults.PermissionMode,
        Schedule = defaults.Schedule,
        SelectedGitAction = defaults.GitAction,
        Draft = defaults.Draft,
    };

    public static NewTaskForm From(Card card) => new()
    {
        Title = card.Title,
        Prompt = card.InitialPrompt,
        WorkingDir = card.WorkingDir,
        Agent = card.AgentType,
        Model = card.LaunchConfig.Model,
        Effort = card.LaunchConfig.Effort,
        Permission = card.LaunchConfig.PermissionMode,
        Schedule = card.Schedule ?? TaskSchedule.Manual,
        ScheduledFor = card.ScheduledFor?.LocalDateTime,
        SelectedGitAction = card.AutoGit?.Action,
        Draft = card.AutoGit?.Draft ?? false,
        AllowConcurrentWorkingDir = card.AllowConcurrentWorkingDir,
    };

    public Card ToCard(DateTimeOffset createdAt)
    {
        var card = new Card
        {
            Column = BoardColumn.Preparing,
            Origin = TaskOrigin.Manual,
            CreatedAt = createdAt,
        };

        ApplyTo(card);

        return card;
    }

    // Writes only the fields this form owns, so everything the board and the agent
    // maintain — id, number, session, column, badge, lineage, transitions, metrics — is
    // preserved when an existing card is edited.
    //
    // Past the launch boundary it writes less still: the gate lives here rather than only in the
    // markup, so a card's prompt is immutable because the model says so and not because an input
    // was rendered disabled.
    public void ApplyTo(Card card)
    {
        card.Title = Titled();

        if (TaskEditing.CanEditLaunchInputs(card))
        {
            card.InitialPrompt = Prompt.Trim();
            card.WorkingDir = WorkingDir.Trim();
            card.AgentType = Agent;

            // Re-armed from scratch whenever the schedule itself changes: an instant resolved
            // against "next window" is meaningless once the user has asked for something else, and
            // nothing on the card would say it was stale.
            if (card.Schedule != Schedule)
                card.EligibleAt = null;

            card.Schedule = Schedule;
            card.ScheduledFor = ScheduledAt();
            card.AutoGit = GitOptions();
            card.AllowConcurrentWorkingDir = AllowConcurrentWorkingDir;
        }

        // Only what the *task* owns. The binary, the extra flags and the environment describe this
        // machine's install and live in settings, so writing them here would be re-inventing the
        // per-card copy `LaunchComposition` exists to stop reading.
        card.LaunchConfig = new LaunchConfig
        {
            Model = Cleaned(Model),
            Effort = Cleaned(Effort),
            PermissionMode = Permission,
        };
    }

    // A card is never written untitled, and the guarantee lives here rather than only in the page for
    // the same reason the launch-boundary gate does: the *code that writes the card* is the only place
    // that can promise it. The title is optional to type because the page offers to write one from the
    // prompt — but that goes out to a CLI, and a CLI can be missing, unauthenticated, out of quota or
    // simply slow. When it is, this is what the board gets: the prompt's own opening words, which is
    // what the page would have shown anyway. An empty string is the one value a title must never be,
    // because a nameless strip is unrecognisable and unsearchable and there is no screen that repairs it.
    private string Titled()
        => Title.Trim() is { Length: > 0 } typed ? typed : TaskTitleQuery.FromPrompt(Prompt);

    private DateTimeOffset? ScheduledAt()
        => WantsDateTime && ScheduledFor is { } when
            ? new DateTimeOffset(when, TimeZoneInfo.Local.GetUtcOffset(when))
            : null;

    // `Draft` only means anything for a pull request, so it is dropped rather than stored
    // against an action that cannot express it.
    private AutoGitOptions? GitOptions()
        => SelectedGitAction is { } action
            ? new AutoGitOptions { Action = action, Draft = Draft && action is GitAction.PullRequest }
            : null;

    private static string? Cleaned(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
