using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.Agents.Codex;

// Codex's rollout file, and for now the *only* thing that reports a Codex session at all: its hooks do
// not fire on the pinned CLI, so this fold does the work Claude Code's hooks do. Measured against real
// rollout files on 2026-07-31 (`codex-cli 0.146.0-alpha.3.1`), where the line kinds that matter are:
//
//   * `event_msg/task_started`   — a turn began (`turn_id`, `model_context_window`)
//   * `event_msg/task_complete`  — a turn ended (`last_agent_message`, and an `error` when it failed)
//   * `event_msg/token_count`    — `total_token_usage`, `last_token_usage`, `model_context_window`
//   * `event_msg/agent_message`  — what the agent said
//   * `response_item/function_call` — a tool call, with its name
//
// **When a CLI build fires Codex hooks, cut the events out of this class** and keep only enrichment,
// the way `ClaudeCodeTranscriptNormalizer` already works — hooks report liveness first-hand and are
// not a second or two behind a file. What cannot come from here either way is a *waiting prompt*:
// nothing is written while the TUI blocks on one, which is why a Codex card never badges
// `needs permission` or `needs answer` until the hooks work.
public sealed class CodexTranscriptNormalizer : ITranscriptNormalizer
{
    private const int LastMessageLimit = 400;

    public AgentType Agent => AgentType.Codex;

    public TranscriptFold Fold(EnrichmentSnapshot snapshot, IReadOnlyList<string> lines)
    {
        var events = new List<AgentEvent>();
        var turns = snapshot.TurnCount ?? 0;
        var tools = snapshot.ToolCalls ?? 0;
        var session = string.Empty;

        foreach (var line in lines)
        {
            if (Parse(line) is not { } entry)
                continue;

            var at = Stamp(entry) ?? DateTimeOffset.UnixEpoch;
            var payload = Payload(entry);

            switch (Text(entry, "type"))
            {
                case "session_meta" when payload is { } meta:
                    session = Text(meta, "session_id") ?? Text(meta, "id") ?? session;

                    break;

                case "event_msg" when payload is { } message:
                    switch (Text(message, "type"))
                    {
                        case "task_started":
                            events.Add(new ActivityObserved(session, at));
                            snapshot = snapshot with { ContextLimit = Count(message, "model_context_window") ?? snapshot.ContextLimit };

                            break;

                        // The turn's own report of how it went. An `error` here is the CLI telling ACT
                        // the turn failed while the process itself stays alive and exits zero — the one
                        // failure `ProcessExited` cannot see.
                        case "task_complete":
                            turns++;
                            snapshot = snapshot with { LastMessage = Bounded(Text(message, "last_agent_message")) ?? snapshot.LastMessage };
                            events.Add(Failure(message) is { } failure
                                ? new TurnFailed(session, at, failure)
                                : new TurnEnded(session, at));

                            break;

                        case "agent_message":
                            snapshot = snapshot with { LastMessage = Bounded(Text(message, "message")) ?? snapshot.LastMessage };

                            break;

                        case "token_count":
                            snapshot = Tokens(snapshot, message);

                            break;
                    }

                    break;

                case "response_item" when payload is { } item && Text(item, "type") is "function_call":
                    tools++;
                    events.Add(new ActivityObserved(session, at, Text(item, "name")));

                    break;
            }
        }

        // Absolute rather than incremented, so a catch-up read of a session already in progress lands
        // on the right numbers instead of doubling what was stored.
        return new TranscriptFold(
            snapshot with { TurnCount = turns, ToolCalls = tools },
            events);
    }

    // Codex reports usage in two shapes and ACT wants one of each: the cumulative totals for tokens,
    // and the *last* request's input for how full the window was. Cached input is excluded from tokens
    // for the same reason as Claude Code — a resumed prefix re-read every turn is not work — but kept
    // in the context figure, because it occupies the window all the same.
    private static EnrichmentSnapshot Tokens(EnrichmentSnapshot snapshot, JsonElement message)
    {
        if (!message.TryGetProperty("info", out var info) || info.ValueKind is not JsonValueKind.Object)
            return snapshot;

        var total = Usage(info, "total_token_usage");
        var last = Usage(info, "last_token_usage");

        return snapshot with
        {
            TokensIn = total is { } t ? t.Input - t.Cached : snapshot.TokensIn,
            TokensOut = total is { } o ? o.Output : snapshot.TokensOut,
            ContextUsed = last is { Input: > 0 } l ? (int)l.Input : snapshot.ContextUsed,
            ContextLimit = Count(info, "model_context_window") ?? snapshot.ContextLimit,
        };
    }

    private static TokenUsage? Usage(JsonElement info, string name)
    {
        if (!info.TryGetProperty(name, out var usage) || usage.ValueKind is not JsonValueKind.Object)
            return null;

        return new TokenUsage(
            Number(usage, "input_tokens"),
            Number(usage, "cached_input_tokens"),
            Number(usage, "output_tokens"));
    }

    // The message is json-in-json — an api error body the CLI captured verbatim. ACT stores the
    // sentence, not the envelope, and falls back to the envelope when it cannot find one.
    private static string? Failure(JsonElement message)
    {
        if (!message.TryGetProperty("error", out var error) || error.ValueKind is not JsonValueKind.Object)
            return null;

        var text = Text(error, "message");

        if (text is null)
            return Text(error, "codex_error_info") ?? "failed";

        try
        {
            using var document = JsonDocument.Parse(text);

            if (document.RootElement.TryGetProperty("error", out var inner)
                && inner.ValueKind is JsonValueKind.Object
                && Text(inner, "message") is { } detail)
                return Bounded(detail);
        }
        catch (JsonException)
        {
        }

        return Bounded(text);
    }

    private static string? Bounded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        return text.Length > LastMessageLimit ? text[..LastMessageLimit] : text;
    }

    private static JsonElement? Payload(JsonElement entry)
        => entry.TryGetProperty("payload", out var payload) && payload.ValueKind is JsonValueKind.Object
            ? payload
            : null;

    private static DateTimeOffset? Stamp(JsonElement entry)
        => Text(entry, "timestamp") is { } text
            && DateTimeOffset.TryParse(
                text,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var stamp)
            ? stamp
            : null;

    private static JsonElement? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);

            return document.RootElement.ValueKind is JsonValueKind.Object
                ? document.RootElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? Count(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;

    private static long Number(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : 0;

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct TokenUsage(long Input, long Cached, long Output);
}
