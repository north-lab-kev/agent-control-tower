using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

// Claude Code's hook payloads in ACT's vocabulary. Two rules shape all of it: an event ACT does
// not recognise normalizes to nothing rather than to an error, and nothing here ever produces a
// decision — `PreToolUse` becomes "the session is doing something", never "allow this tool".
public sealed class ClaudeCodeHookNormalizer : IHookNormalizer
{
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
            "PreToolUse" or "PostToolUse" => [new ActivityObserved(id, at, Text(payload, "tool_name"))],
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

    // `Notification` is the only thing that reports a waiting permission prompt now that ACT does
    // not read the screen — and it is not self-describing. Measured on 2026-07-30 against
    // `claude-code v2.1.220`: it *also* fires an idle nudge whose message is exactly
    // **"Claude is waiting for your input"**, roughly a minute after a turn ends.
    //
    // So the message is all there is to go on, and an unrecognised one produces **no event at
    // all**. Neither alternative is safe: calling it a permission request overwrites a reviewable
    // card's `to review` with `needs permission` and nothing to approve (the bug this replaces),
    // and calling it activity is worse still — an idle nudge means the opposite of activity, and
    // would send a reviewed card back to Executing on its own.
    private static AgentEvent[] Notification(JsonElement payload, string sessionId, DateTimeOffset at)
    {
        var message = Text(payload, "message") ?? string.Empty;

        var looksLikePermission = message.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || message.Contains("approve", StringComparison.OrdinalIgnoreCase);

        if (!looksLikePermission)
            return [];

        return
        [
            new PermissionRequested(
                sessionId,
                at,
                RequestId: $"{sessionId}:{at.ToUnixTimeMilliseconds()}",
                Summary: message),
        ];
    }

    private static string? Text(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
