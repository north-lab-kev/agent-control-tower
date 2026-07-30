using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.Agents.Codex;

// PENDING — WRITTEN BLIND, like `CodexHookConfig`. No Codex hook payload has ever been observed
// arriving, because hooks do not fire on the pinned CLI; the field names below come from the
// documented payload contract, not from a captured request. See `docs/codex-hooks-findings.md`.
//
// The one place Codex is richer than Claude Code: `PermissionRequest` is an explicit event, so a
// waiting prompt is reported rather than inferred from a notification message.
public sealed class CodexHookNormalizer : IHookNormalizer
{
    public AgentType Agent => AgentType.Codex;

    public HookNormalization Normalize(JsonElement payload, string? knownSessionId, DateTimeOffset at)
    {
        if (payload.ValueKind is not JsonValueKind.Object)
            return HookNormalization.None;

        // Codex mints its own id, so this is how the card's binding arrives — the reason the
        // normalizer returns a session id at all rather than only events.
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
            "PermissionRequest" =>
            [
                new PermissionRequested(
                    id,
                    at,
                    RequestId: Text(payload, "turn_id") ?? $"{id}:{at.ToUnixTimeMilliseconds()}",
                    Summary: Summary(payload)),
            ],
            "PreCompact" => [new CompactingStarted(id, at)],
            "PostCompact" => [new CompactingFinished(id, at)],
            "Stop" => [new TurnEnded(id, at, TurnOutcome.Unknown)],
            _ => [],
        };

        return new HookNormalization(sessionId, events);
    }

    // Read-only and deliberately short: what the card shows is a statement that something is
    // waiting, not a command dump the user might mistake for something ACT can answer.
    private static string Summary(JsonElement payload)
        => Text(payload, "reason")
            ?? Text(payload, "message")
            ?? Text(payload, "tool_name")
            ?? "waiting for approval";

    private static string? Text(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
