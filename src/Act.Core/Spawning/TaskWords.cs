using Act.Core.Model;

namespace Act.Core.Spawning;

// The enum vocabulary the MCP tools speak. Written out rather than `ToString()`-ed for two reasons:
// these values are a published API an agent writes back into `create_followup`, so they must not
// move when an enum member is renamed; and they are the one thing ACT says to an agent that is
// **not** localised — a machine reads them.
//
// One table per vocabulary, from which the emit (`Of`), the parse (`TryParse`) and the valid-word
// list a refusal shows all derive — so a new member is one row, and a word an agent is given is a
// word the parse accepts. A row marked `Emitted: false` (aliases) is read but never written; one
// marked `Accepted: false` (`scheduled`) is written but never read.
public static class TaskWords
{
    private sealed record Word<T>(T Value, string Text, string[] Aliases, bool Accepted = true);

    private static readonly Word<BoardColumn>[] Columns =
    [
        new(BoardColumn.Preparing, "preparing", []),
        new(BoardColumn.Ready, "ready", []),
        new(BoardColumn.Executing, "executing", []),
        new(BoardColumn.YourTurn, "your_turn", ["yourturn", "your turn"]),
        new(BoardColumn.Completed, "completed", []),
    ];

    private static readonly Word<AgentType>[] Agents =
    [
        new(AgentType.ClaudeCode, "claude", ["claudecode", "claude-code", "claude_code"]),
        new(AgentType.Codex, "codex", []),
    ];

    private static readonly Word<PermissionMode>[] Permissions =
    [
        new(PermissionMode.Default, "default", []),
        new(PermissionMode.Plan, "plan", []),
        new(PermissionMode.AcceptEdits, "acceptEdits", ["accept_edits"]),
        new(PermissionMode.Auto, "auto", []),
        new(PermissionMode.DontAsk, "dontAsk", ["dont_ask"]),
        new(PermissionMode.Bypass, "bypass", ["bypasspermissions", "bypass_permissions"]),
    ];

    private static readonly Word<TaskSchedule>[] Schedules =
    [
        new(TaskSchedule.Manual, "manual", []),
        new(TaskSchedule.Now, "now", []),
        new(TaskSchedule.NextWindow, "next_window", ["nextwindow", "next window"]),
        new(TaskSchedule.SpecificDateTime, "scheduled", [], Accepted: false),
    ];

    public static string ColumnWords { get; } = Offered(Columns);

    public static string AgentWords { get; } = Offered(Agents);

    public static string PermissionWords { get; } = Offered(Permissions);

    public static string ScheduleWords { get; } = Offered(Schedules);

    public static string Of(BoardColumn column) => Emitted(Columns, column, "unknown");

    public static string Of(AgentType agent) => Emitted(Agents, agent, "unknown");

    public static string Of(PermissionMode mode) => Emitted(Permissions, mode, "default");

    public static string Of(TaskSchedule schedule) => Emitted(Schedules, schedule, "manual");

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

    public static bool TryParse(string? text, out BoardColumn column) => TryMatch(Columns, text, out column);

    public static bool TryParse(string? text, out AgentType agent) => TryMatch(Agents, text, out agent);

    public static bool TryParse(string? text, out PermissionMode mode) => TryMatch(Permissions, text, out mode);

    public static bool TryParse(string? text, out TaskSchedule schedule) => TryMatch(Schedules, text, out schedule);

    private static string Emitted<T>(Word<T>[] table, T value, string fallback)
        => table.FirstOrDefault(word => EqualityComparer<T>.Default.Equals(word.Value, value))?.Text ?? fallback;

    private static bool TryMatch<T>(Word<T>[] table, string? text, out T value)
    {
        var typed = text?.Trim() ?? string.Empty;

        foreach (var word in table)
        {
            if (!word.Accepted)
                continue;

            if (Matches(word.Text, typed) || word.Aliases.Any(alias => Matches(alias, typed)))
            {
                value = word.Value;

                return true;
            }
        }

        value = default!;

        return false;
    }

    private static bool Matches(string candidate, string typed)
        => string.Equals(candidate, typed, StringComparison.OrdinalIgnoreCase);

    private static string Offered<T>(Word<T>[] table)
        => string.Join(", ", table.Where(word => word.Accepted).Select(word => word.Text));
}
