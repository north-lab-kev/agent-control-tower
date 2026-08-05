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

    // The card id is minted here rather than by `ToCard`, because a file is written the moment it is
    // attached and it has to be written *somewhere*: the id is the attachment directory's name, so
    // the folder a create stages into is already the folder the saved card owns and there is no move
    // step to get wrong.
    public Guid CardId { get; init; } = Guid.NewGuid();

    // Deliberately outside the record's own equality — a `List<T>` compares by reference, so the
    // dirty check ignores it and `TaskView` compares the names itself. Attached files are on disk
    // before the form is saved, which is why they are not "unsaved changes" in the same sense the
    // text fields are.
    public List<TaskAttachment> Attachments { get; init; } = [];

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

    // A new task starts as a template. The title comes across when the template carries one, and a
    // template that leaves it blank falls through to the usual answer — written from the prompt on
    // save, or whatever the user types first.
    public static NewTaskForm From(TaskTemplate template) => new()
    {
        Title = template.Title,
        Prompt = template.Prompt,
        WorkingDir = template.WorkingDir,
        Agent = template.Agent,
        Model = template.Model,
        Effort = template.Effort,
        Permission = template.PermissionMode,
        Schedule = template.Schedule,
        SelectedGitAction = template.GitAction,
        Draft = template.Draft,
        AllowConcurrentWorkingDir = template.AllowConcurrentWorkingDir,
    };

    // The fields on screen rather than the saved card, unlike Duplicate: a copy has to be the run
    // that was saved, but a template is not a run — it is the shape of the form in front of you, and
    // it can be captured on a task that has never been saved at all. The one thing dropped is the
    // schedule's specific datetime, which is a moment rather than a habit: a template holding one
    // would arm every task it made for a time that has already passed.
    public TaskTemplate ToTemplate(string name) => new()
    {
        Name = name.Trim(),
        Title = Title.Trim(),
        Prompt = Prompt.Trim(),
        WorkingDir = WorkingDir.Trim(),
        Agent = Agent,
        Model = Cleaned(Model),
        Effort = Cleaned(Effort),
        PermissionMode = Permission,
        Schedule = WantsDateTime ? TaskSchedule.Manual : Schedule,
        GitAction = SelectedGitAction,
        Draft = Draft && SelectedGitAction is GitAction.PullRequest,
        AllowConcurrentWorkingDir = AllowConcurrentWorkingDir,
    };

    public static NewTaskForm From(Card card) => new()
    {
        CardId = card.Id,
        Attachments = [.. card.Attachments],
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
            Id = CardId,
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
            card.Attachments = [.. Attachments];
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
