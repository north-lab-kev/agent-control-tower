using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

// Claude Code's hook payloads in ACT's vocabulary. Two rules shape all of it: an event ACT does
// not recognise normalizes to nothing rather than to an error, and nothing here ever produces a
// decision — `PreToolUse` reports what the session is about to do, never "allow this tool".
public sealed class ClaudeCodeHookNormalizer : IHookNormalizer
{
    // The tool whose whole purpose is to put a question to the user. The CLI blocks on it behind an
    // ordinary permission prompt, so the `Notification` it fires is byte-for-byte the one a `Bash`
    // approval fires — the tool name is the only thing that tells a question from a permission, and
    // `tool_input` is the only place the question text exists at all.
    private const string QuestionTool = "AskUserQuestion";

    private const string PermissionPrompt = "permission_prompt";

    public AgentType Agent => AgentType.ClaudeCode;

    public HookNormalization Normalize(JsonElement payload, string? knownSessionId, DateTimeOffset at)
    {
        if (payload.ValueKind is not JsonValueKind.Object)
            return HookNormalization.None;

        var sessionId = Text(payload, "session_id") ?? knownSessionId;
        var name = Text(payload, "hook_event_name");

        if (name is null)
            return new HookNormalization(sessionId, []);

        var id = sessionId ?? string.Empty;

        AgentEvent[] events = name switch
        {
            "SessionStart" =>
            [
                new SessionStarted(
                    id,
                    at,
                    Text(payload, "transcript_path") ?? string.Empty,
                    Text(payload, "cwd") ?? string.Empty),
            ],
            "SessionEnd" => [new SessionEnded(id, at)],
            "UserPromptSubmit" => [new ActivityObserved(id, at)],
            "PreToolUse" => ToolAboutToRun(payload, id, at),
            "PostToolUse" => [new ActivityObserved(id, at, Text(payload, "tool_name"))],
            "PreCompact" => [new CompactingStarted(id, at)],
            "Notification" => Notification(payload, id, at),

            // The turn's *outcome* is not in this payload — it comes from the status file the
            // preamble asks the agent to write, which the file source reads. `Stop` only says the
            // turn ended, so `Unknown` here is the honest value and the rules engine treats it as
            // ready-for-review when no status file contradicts it.
            "Stop" => [new TurnEnded(id, at, TurnOutcome.Unknown)],
            _ => [],
        };

        return new HookNormalization(sessionId, events);
    }

    // Most tools mean "the session is doing something". `AskUserQuestion` means the opposite: the
    // agent has stopped and is waiting on the user, and this hook is the *only* payload that says
    // what it asked. The `Notification` that follows a beat later cannot — see below.
    private static AgentEvent[] ToolAboutToRun(JsonElement payload, string sessionId, DateTimeOffset at)
    {
        var tool = Text(payload, "tool_name");

        if (tool != QuestionTool)
            return [new ActivityObserved(sessionId, at, tool)];

        return
        [
            new QuestionAsked(
                sessionId,
                at,
                RequestId: Text(payload, "tool_use_id") ?? Moment(sessionId, at),
                Question: Questions(payload) ?? string.Empty),
        ];
    }

    // Every question in the payload, because one `AskUserQuestion` call may carry several and the
    // card should not silently show the first of them as if it were the whole ask.
    private static string? Questions(JsonElement payload)
    {
        if (!payload.TryGetProperty("tool_input", out var input)
            || input.ValueKind is not JsonValueKind.Object
            || !input.TryGetProperty("questions", out var questions)
            || questions.ValueKind is not JsonValueKind.Array)
            return null;

        var asked = string.Join(
            " · ",
            questions.EnumerateArray()
                .Where(question => question.ValueKind is JsonValueKind.Object)
                .Select(question => Text(question, "question"))
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        return asked.Length is 0 ? null : asked;
    }

    // `Notification` reports a waiting permission prompt, and it is not the only thing it reports —
    // but it does say which kind it is. Measured on 2026-07-30 against `claude-code v2.1.220`:
    //
    //   * a waiting permission prompt → `notification_type: "permission_prompt"`,
    //     message *"Claude needs your permission"*
    //   * the idle nudge about a minute after a turn ends → `notification_type: "idle_prompt"`,
    //     message *"Claude is waiting for your input"*
    //
    // The type is what ACT keys on, because the message cannot carry the distinction that matters:
    // an `AskUserQuestion` prompt is announced as `permission_prompt` with that same wording, and
    // only `PreToolUse` knows it was a question. Suppressing that second notice is the rules
    // engine's job — a permission request never overwrites an unanswered question.
    //
    // An unrecognised type produces **no event at all**. Neither alternative is safe: calling it a
    // permission request badges an idle card `needs permission` with nothing to approve, and calling
    // it activity is worse still — an idle nudge means the opposite of activity, and would send a
    // reviewed card back to Executing on its own. The message check survives only as the fallback
    // for a payload that omits the type.
    private static AgentEvent[] Notification(JsonElement payload, string sessionId, DateTimeOffset at)
    {
        var message = Text(payload, "message") ?? string.Empty;

        if (!IsPermissionPrompt(Text(payload, "notification_type"), message))
            return [];

        return
        [
            new PermissionRequested(
                sessionId,
                at,
                RequestId: Moment(sessionId, at),
                Summary: message),
        ];
    }

    private static bool IsPermissionPrompt(string? type, string message) => type switch
    {
        PermissionPrompt => true,
        null => message.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || message.Contains("approve", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static string Moment(string sessionId, DateTimeOffset at)
        => $"{sessionId}:{at.ToUnixTimeMilliseconds()}";

    private static string? Text(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
