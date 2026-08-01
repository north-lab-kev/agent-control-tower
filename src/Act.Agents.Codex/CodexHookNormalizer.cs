using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.Agents.Codex;

// Written blind against the documented payload contract, and **proven against real payloads on
// 2026-07-31**: `SessionStart`, `UserPromptSubmit`, the tool events, `Stop` and `PermissionRequest`
// all normalized correctly on the first live run. See `docs/codex-hooks-findings.md`.
//
// The one place Codex is richer than Claude Code: `PermissionRequest` is an explicit event, so a
// waiting prompt is reported rather than inferred from a notification message. Measured live as
// `apply_patch` on a write approval, raising `needs permission` from a prompt ACT never touched.
public sealed class CodexHookNormalizer : IHookNormalizer
{
    // Codex's own ask-the-user tool, named in its base instructions: *"Use the `request_user_input`
    // tool only when it is listed in the available tools for this turn"*. In Default mode it is told to
    // prefer assumptions over asking, so this fires rarely — which is exactly why a card must not
    // report `running` when it does.
    private const string QuestionTool = "request_user_input";

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
            return new HookNormalization(sessionId, [], Text(payload, "transcript_path"));

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
            "Stop" => [new TurnEnded(id, at)],
            _ => [],
        };

        return new HookNormalization(sessionId, events, Text(payload, "transcript_path"));
    }

    // Most tools mean the session is working. `request_user_input` means the opposite — Codex has
    // stopped and is waiting on the user — so it has to badge `needs answer`, not `running`. Exactly
    // the distinction `ClaudeCodeHookNormalizer` draws for `AskUserQuestion`, and for the same reason:
    // without it a blocked card reports activity and sits in Executing while nobody answers it.
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
                RequestId: Text(payload, "tool_call_id")
                    ?? Text(payload, "turn_id")
                    ?? $"{sessionId}:{at.ToUnixTimeMilliseconds()}",
                Question: Question(payload) ?? string.Empty),
        ];
    }

    // The argument shape was read off a real `function_call` line in the rollout rather than guessed;
    // the fallbacks are there because one field name is not worth a wrong badge if the CLI renames it.
    private static string? Question(JsonElement payload)
    {
        if (!payload.TryGetProperty("tool_input", out var input)
            || input.ValueKind is not JsonValueKind.Object)
            return null;

        if ((Text(input, "prompt") ?? Text(input, "question")) is { } single)
            return single;

        if (!input.TryGetProperty("questions", out var questions)
            || questions.ValueKind is not JsonValueKind.Array)
            return null;

        var asked = string.Join(
            " · ",
            questions.EnumerateArray()
                .Select(question => question.ValueKind is JsonValueKind.String
                    ? question.GetString()
                    : Text(question, "question") ?? Text(question, "prompt"))
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        return asked.Length == 0 ? null : asked;
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
