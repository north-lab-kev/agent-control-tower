using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.Agents.Codex;

// Codex's rollout file, read for enrichment — exactly the role `ClaudeCodeTranscriptNormalizer` has.
// Measured against real rollout files (`codex-cli 0.146.0-alpha.3.1`), where the line
// kinds that matter are:
//
//   * `event_msg/task_started`   — `model_context_window`
//   * `event_msg/task_complete`  — an `error` when the turn failed
//   * `event_msg/token_count`    — `total_token_usage`, `last_token_usage`, `model_context_window`
//
// **Hooks own the counts; the transcript owns enrichment.** Liveness — activity, turn ends, the
// turn and tool counts — is reported first-hand by hooks rather than a poll behind a file.
//
// The one event raised here, and the reason it is an exception: `TurnFailed`. `task_complete`
// carrying an `error` is the CLI saying the turn failed while its process stays alive and would
// exit zero — the failure `ProcessExited` cannot see — and no hook has been *observed* reporting
// it. Measured, not assumed: drop this only after watching a real failed turn's `Stop` payload.
public sealed class CodexTranscriptNormalizer : ITranscriptNormalizer
{
    private const int FailureLimit = 400;

    public AgentType Agent => AgentType.Codex;

    public TranscriptFold Fold(EnrichmentSnapshot snapshot, IReadOnlyList<string> lines)
    {
        var events = new List<AgentEvent>();
        var session = string.Empty;

        // Each line is read inside its document's own scope — only strings and numbers leave it —
        // so no per-line `Clone` of the parsed DOM is paid on a catch-up read of a whole rollout.
        foreach (var line in lines)
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                if (document.RootElement.ValueKind is JsonValueKind.Object)
                    snapshot = FoldEntry(document.RootElement, snapshot, events, ref session);
            }
        }

        return new TranscriptFold(snapshot, events);
    }

    private static EnrichmentSnapshot FoldEntry(
        JsonElement entry,
        EnrichmentSnapshot snapshot,
        List<AgentEvent> events,
        ref string session)
    {
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
                        snapshot = snapshot with { ContextLimit = Count(message, "model_context_window") ?? snapshot.ContextLimit };

                        break;

                    // A plain turn end is the `Stop` hook's to report. Only the failure is raised
                    // here, because nothing else can see it.
                    case "task_complete":
                        if (Failure(message) is { } failure)
                            events.Add(new TurnFailed(session, at, failure));

                        break;

                    case "token_count":
                        snapshot = Tokens(snapshot, message);

                        break;
                }

                break;
        }

        return snapshot;
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

        return text.Length > FailureLimit ? text[..FailureLimit] : text;
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
