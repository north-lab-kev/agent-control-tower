using Act.Core.Model;

namespace Act.Core.Spawning;

// The enum vocabulary the MCP tools speak. Written out rather than `ToString()`-ed for two reasons:
// these values are a published API an agent writes back into `create_followup`, so they must not
// move when an enum member is renamed; and they are the one thing ACT says to an agent that is
// **not** localised — a machine reads them.
public static class TaskWords
{
    public static string Of(BoardColumn column) => column switch
    {
        BoardColumn.Preparing => "preparing",
        BoardColumn.Ready => "ready",
        BoardColumn.Executing => "executing",
        BoardColumn.YourTurn => "your_turn",
        BoardColumn.Completed => "completed",
        _ => "unknown",
    };

    public static string? Of(Badge? badge) => badge switch
    {
        Model.Badge.Running => "running",
        Model.Badge.Compacting => "compacting",
        Model.Badge.NeedsPermission => "needs_permission",
        Model.Badge.NeedsAnswer => "needs_answer",
        Model.Badge.Error => "error",
        Model.Badge.Killed => "killed",
        Model.Badge.ReadyForReview => "to_review",
        _ => null,
    };

    public static string Of(AgentType agent) => agent switch
    {
        AgentType.ClaudeCode => "claude",
        AgentType.Codex => "codex",
        _ => "unknown",
    };

    public static string Of(PermissionMode mode) => mode switch
    {
        PermissionMode.Default => "default",
        PermissionMode.Plan => "plan",
        PermissionMode.AcceptEdits => "acceptEdits",
        PermissionMode.Auto => "auto",
        PermissionMode.DontAsk => "dontAsk",
        PermissionMode.Bypass => "bypass",
        _ => "default",
    };

    public static string Of(TaskSchedule schedule) => schedule switch
    {
        TaskSchedule.Manual => "manual",
        TaskSchedule.Now => "now",
        TaskSchedule.NextWindow => "next_window",
        TaskSchedule.SpecificDateTime => "scheduled",
        _ => "manual",
    };

    public static string? Of(AutoGitOptions? autoGit) => autoGit?.Action switch
    {
        GitAction.Commit => "commit",
        GitAction.Push => "push",
        GitAction.PullRequest => "pr",
        _ => null,
    };
}
