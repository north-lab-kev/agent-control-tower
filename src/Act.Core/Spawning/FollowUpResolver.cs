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
        var dependsOn = ResolveDependencies(request.DependsOn, rejections);
        var permission = ResolvePermission(parent, request.Permission, rejections);

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
            WorkingDir = string.IsNullOrWhiteSpace(request.WorkingDir)
                ? parent.WorkingDir
                : request.WorkingDir.Trim(),
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

        return requested!.Trim().ToLowerInvariant() switch
        {
            "claude" or "claudecode" or "claude-code" or "claude_code" => AgentType.ClaudeCode,
            "codex" => AgentType.Codex,
            _ => Refuse(rejections, parent.AgentType, $"Agent '{requested}' is not one of: same, claude, codex."),
        };
    }

    // No `same`, and the omission is the design: a parent's schedule is a trigger that already
    // fired, so inheriting it would mean either a date in the past or a child that launches the
    // instant it lands. `SpecificDateTime` is absent for the same class of reason — there is no
    // datetime argument for it to pair with.
    private static TaskSchedule ResolveSchedule(string? requested, List<string> rejections)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return TaskSchedule.Manual;

        return requested.Trim().ToLowerInvariant() switch
        {
            "manual" => TaskSchedule.Manual,
            "now" => TaskSchedule.Now,
            "next_window" or "nextwindow" or "next window" => TaskSchedule.NextWindow,
            _ => Refuse(
                rejections,
                TaskSchedule.Manual,
                $"Schedule '{requested}' is not one of: manual, now, next_window."),
        };
    }

    private static PermissionMode ResolvePermission(Card parent, string? requested, List<string> rejections)
    {
        if (FollowUpRequest.Inherits(requested))
            return parent.LaunchConfig.PermissionMode;

        return requested!.Trim().ToLowerInvariant() switch
        {
            "default" => PermissionMode.Default,
            "plan" => PermissionMode.Plan,
            "acceptedits" or "accept_edits" => PermissionMode.AcceptEdits,
            "auto" => PermissionMode.Auto,
            "dontask" or "dont_ask" => PermissionMode.DontAsk,
            "bypass" or "bypasspermissions" => PermissionMode.Bypass,
            _ => Refuse(
                rejections,
                PermissionMode.Default,
                $"Permission '{requested}' is not one of: same, default, plan, acceptEdits, auto, "
                    + "dontAsk, bypass."),
        };
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

        return requested!.Trim().ToLowerInvariant() switch
        {
            "none" => null,
            "commit" => new AutoGitOptions { Action = GitAction.Commit },
            "push" => new AutoGitOptions { Action = GitAction.Push },
            "pr" or "pullrequest" or "pull_request" => new AutoGitOptions { Action = GitAction.PullRequest },
            _ => Refuse<AutoGitOptions?>(
                rejections,
                null,
                $"Git action '{requested}' is not one of: same, none, commit, push, pr."),
        };
    }

    private static List<Guid> ResolveDependencies(IReadOnlyList<string>? requested, List<string> rejections)
    {
        var resolved = new List<Guid>();

        foreach (var id in requested ?? [])
        {
            if (Guid.TryParse(id, out var parsed))
                resolved.Add(parsed);
            else
                rejections.Add($"'{id}' is not a task id. Use the id `create_followup` returned, or one "
                    + "from `list_tasks`.");
        }

        return resolved;
    }

    // The rejection is what the caller reads; the value returned is only there to keep parsing going,
    // so every bad argument in one call is reported at once rather than one round trip each.
    private static T Refuse<T>(List<string> rejections, T placeholder, string message)
    {
        rejections.Add(message);

        return placeholder;
    }
}
