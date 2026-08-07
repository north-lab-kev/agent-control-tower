using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.Core.Spawning;

// A `create_followup` call turned into the card it asks for, or into the reasons it cannot be.
// Everything the agent may choose is parsed here, and everything it left as `same` is taken from the
// parent — so the one place that decides what a follow-up inherits is also the one place that
// decides what a bad value is answered with.
//
// The launch knobs themselves are handed to `LaunchConfigResolver` rather than re-validated, which
// is what keeps a spawned card and a hand-made one subject to the same rule.
public static class FollowUpResolver
{
    public static FollowUpResolution Resolve(
        Card parent,
        FollowUpRequest request,
        IAgentCapabilityCatalog catalog,
        IWorkingDirectories directories,
        IReadOnlyList<Card> cards,
        DateTimeOffset now)
    {
        var rejections = new List<string>();

        var title = request.Title?.Trim() ?? string.Empty;
        var prompt = request.Prompt?.Trim() ?? string.Empty;

        if (title.Length == 0)
            rejections.Add("A follow-up needs a title.");

        if (prompt.Length == 0)
            rejections.Add("A follow-up needs a prompt.");

        var agent = ResolveAgent(parent, request.Agent, rejections);
        var schedule = ResolveSchedule(request.Schedule, rejections);
        var autoGit = ResolveAutoGit(parent, request.AutoGit, rejections);
        var dependsOn = ResolveDependencies(request.DependsOn, cards, rejections);
        var permission = ResolvePermission(parent, request.Permission, rejections);
        var workingDir = ResolveWorkingDir(parent, request.WorkingDir, directories, rejections);

        if (rejections.Count > 0)
            return FollowUpResolution.Refused([.. rejections]);

        // Model and effort inherit only when the agent is unchanged. A slug is one CLI's vocabulary,
        // so carrying `opus` onto a Codex card would turn every cross-agent spawn into a rejection
        // for a value the agent never chose; across agents `same` means the new one's own default,
        // which is what `LaunchConfigResolver` fills in for a null.
        var crossed = agent != parent.AgentType;

        var config = new LaunchConfig
        {
            Model = Chosen(request.Model, crossed ? null : parent.LaunchConfig.Model),
            Effort = Chosen(request.Effort, crossed ? null : parent.LaunchConfig.Effort),
            PermissionMode = permission,
        };

        var launch = LaunchConfigResolver.Resolve(agent, catalog.For(agent), config);

        if (!launch.CanLaunch)
            return new FollowUpResolution(null, launch.Adjustments, launch.Rejections);

        var card = new Card
        {
            Title = title,
            InitialPrompt = prompt,
            WorkingDir = workingDir,
            AgentType = agent,
            LaunchConfig = launch.Resolved,
            Schedule = schedule,
            AutoGit = autoGit,
            DependsOn = dependsOn,
            Column = BoardColumn.Ready,
            Origin = TaskOrigin.Spawned,
            SpawnAuthor = SpawnAuthor.Agent,
            ParentId = parent.Id,
            CreatedAt = now,
        };

        return new FollowUpResolution(card, launch.Adjustments, []);
    }

    private static string? Chosen(string? requested, string? inherited)
        => FollowUpRequest.Inherits(requested) ? inherited : requested!.Trim();

    private static AgentType ResolveAgent(Card parent, string? requested, List<string> rejections)
    {
        if (FollowUpRequest.Inherits(requested))
            return parent.AgentType;

        return TaskWords.TryParse(requested, out AgentType agent)
            ? agent
            : Refuse(
                rejections,
                parent.AgentType,
                $"Agent '{requested}' is not one of: same, {TaskWords.AgentWords}.");
    }

    // No `same`, and the omission is the design: a parent's schedule is a trigger that already
    // fired, so inheriting it would mean either a date in the past or a child that launches the
    // instant it lands. `SpecificDateTime` is absent for the same class of reason — there is no
    // datetime argument for it to pair with.
    private static TaskSchedule ResolveSchedule(string? requested, List<string> rejections)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return TaskSchedule.Manual;

        return TaskWords.TryParse(requested, out TaskSchedule schedule)
            ? schedule
            : Refuse(
                rejections,
                TaskSchedule.Manual,
                $"Schedule '{requested}' is not one of: {TaskWords.ScheduleWords}.");
    }

    private static PermissionMode ResolvePermission(Card parent, string? requested, List<string> rejections)
    {
        if (FollowUpRequest.Inherits(requested))
            return parent.LaunchConfig.PermissionMode;

        return TaskWords.TryParse(requested, out PermissionMode mode)
            ? mode
            : Refuse(
                rejections,
                PermissionMode.Default,
                $"Permission '{requested}' is not one of: same, {TaskWords.PermissionWords}.");
    }

    // `Draft` rides the action rather than being an argument of its own: it only means anything for
    // a pull request, and an agent that names an action explicitly is choosing the action, not
    // re-affirming the parent's draft preference.
    private static AutoGitOptions? ResolveAutoGit(Card parent, string? requested, List<string> rejections)
    {
        if (FollowUpRequest.Inherits(requested))
            return parent.AutoGit is { } inherited
                ? new AutoGitOptions { Action = inherited.Action, Draft = inherited.Draft }
                : null;

        if (!TaskWords.TryParse(requested, out GitAction? action))
            return Refuse<AutoGitOptions?>(
                rejections,
                null,
                $"Git action '{requested}' is not one of: same, {TaskWords.GitWords}.");

        return action is { } chosen ? new AutoGitOptions { Action = chosen } : null;
    }

    // Existence is checked at creation, where the refusal can still teach the agent something. Once
    // stored, a prerequisite that later disappears counts as satisfied (`DependencyGate`) — but that
    // tolerance is for cards deleted on purpose, not for ids that never named anything: a mistyped
    // GUID accepted here would gate nothing and say nothing, and the ordering the agent asked for
    // would silently never happen.
    private static List<Guid> ResolveDependencies(
        IReadOnlyList<string>? requested,
        IReadOnlyList<Card> cards,
        List<string> rejections)
    {
        var resolved = new List<Guid>();

        foreach (var id in requested ?? [])
        {
            if (!Guid.TryParse(id, out var parsed))
            {
                rejections.Add($"'{id}' is not a task id. Use the id `create_followup` returned, or one "
                    + "from `list_tasks`.");

                continue;
            }

            if (cards.All(card => card.Id != parsed))
            {
                rejections.Add($"'{id}' does not name a task on the board. Use the id `create_followup` "
                    + "returned, or one from `list_tasks`.");

                continue;
            }

            resolved.Add(parsed);
        }

        return resolved;
    }

    // The same rule the task form applies before a card may be saved: a malformed or relative path
    // is refused rather than stored, because a relative one would resolve against whatever directory
    // ACT happens to be running in. Merely missing is allowed, exactly as the form allows it with a
    // warning — the launch is where a directory has to exist.
    private static string ResolveWorkingDir(
        Card parent,
        string? requested,
        IWorkingDirectories directories,
        List<string> rejections)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return parent.WorkingDir;

        var typed = requested.Trim();
        var check = directories.Check(typed);

        if (check.WellFormed)
            return typed;

        return Refuse(
            rejections,
            parent.WorkingDir,
            check.Error == PathError.NotAbsolute
                ? $"Working directory '{typed}' is not an absolute path."
                : $"Working directory '{typed}' is not a usable path.");
    }

    // The rejection is what the caller reads; the value returned is only there to keep parsing going,
    // so every bad argument in one call is reported at once rather than one round trip each.
    private static T Refuse<T>(List<string> rejections, T placeholder, string message)
    {
        rejections.Add(message);

        return placeholder;
    }
}
