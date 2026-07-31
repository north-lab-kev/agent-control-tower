using Act.Core.Model;

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

    // Empty means "resolve the agent's own name off `PATH`", which is the normal case. It exists
    // because one real install shape cannot be launched otherwise: a Store-packaged Codex is not on
    // `PATH` at all, and its runnable copy lives at the `CODEX_CLI_PATH` recorded in
    // `~/.codex/config.toml`.
    public string AgentBinary { get; set; } = string.Empty;

    public string AllowedTools { get; set; } = string.Empty;

    public string DisallowedTools { get; set; } = string.Empty;

    public string ExtraFlags { get; set; } = string.Empty;

    public string Env { get; set; } = string.Empty;

    public bool WantsDateTime => Schedule is TaskSchedule.SpecificDateTime;

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
        AgentBinary = card.LaunchConfig.AgentBinary ?? string.Empty,
        AllowedTools = Joined(card.LaunchConfig.AllowedTools),
        DisallowedTools = Joined(card.LaunchConfig.DisallowedTools),
        ExtraFlags = Joined(card.LaunchConfig.ExtraFlags),
        Env = JoinedEnvironment(card.LaunchConfig.Env),
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
    public void ApplyTo(Card card)
    {
        card.Title = Title.Trim();
        card.InitialPrompt = Prompt.Trim();
        card.WorkingDir = WorkingDir.Trim();
        card.AgentType = Agent;
        card.Schedule = Schedule;
        card.ScheduledFor = ScheduledAt();
        card.AutoGit = GitOptions();
        card.LaunchConfig = new LaunchConfig
        {
            AgentBinary = Cleaned(AgentBinary),
            Model = Cleaned(Model),
            Effort = Cleaned(Effort),
            PermissionMode = Permission,
            AllowedTools = Lines(AllowedTools),
            DisallowedTools = Lines(DisallowedTools),
            ExtraFlags = Lines(ExtraFlags),
            Env = EnvironmentVariables(),
        };
    }

    private DateTimeOffset? ScheduledAt()
        => WantsDateTime && ScheduledFor is { } when
            ? new DateTimeOffset(when, TimeZoneInfo.Local.GetUtcOffset(when))
            : null;

    private static string Joined(IEnumerable<string> values) => string.Join('\n', values);

    private static string JoinedEnvironment(IEnumerable<KeyValuePair<string, string>> variables)
        => string.Join('\n', variables.Select(variable => $"{variable.Key}={variable.Value}"));

    // `Draft` only means anything for a pull request, so it is dropped rather than stored
    // against an action that cannot express it.
    private AutoGitOptions? GitOptions()
        => SelectedGitAction is { } action
            ? new AutoGitOptions { Action = action, Draft = Draft && action is GitAction.PullRequest }
            : null;

    private static string? Cleaned(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Split on newlines and commas only: a tool pattern such as `Bash(git *)` contains a
    // space, so whitespace is not a safe separator here.
    private static IList<string> Lines(string value)
        => [.. value.Split(
            ['\n', '\r', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private IDictionary<string, string> EnvironmentVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in Env.Split(
            ['\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            variables[line[..separator].TrimEnd()] = line[(separator + 1)..].TrimStart();
        }

        return variables;
    }
}
