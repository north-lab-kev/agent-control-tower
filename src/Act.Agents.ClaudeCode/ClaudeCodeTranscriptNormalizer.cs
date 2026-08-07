using System.Text.Json;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

// Claude Code's JSONL transcript, folded into enrichment. Measured against a real transcript on
// 2026-07-30 (`claude-code v2.1.220`): the file holds one json object per line, and only the
// `assistant` lines carry anything ACT wants — `message.model` and a `message.usage` with
// `input_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens` and `output_tokens`.
// Everything else in there (`user`, `attachment`, `system`, the title and queue bookkeeping lines)
// is skipped, and an unparseable line is skipped rather than failing the read — a transcript is
// someone else's format and ACT only ever reads it.
public sealed class ClaudeCodeTranscriptNormalizer : ITranscriptNormalizer
{
    public AgentType Agent => AgentType.ClaudeCode;

    // Enrichment only, and no events: everything about *liveness* — activity, turn ends, prompts — is
    // reported first-hand by hooks, which are neither a poll behind nor dependent on a file being
    // flushed. Both adapters have this shape since 2026-07-31; Codex raises one event here, `TurnFailed`,
    // because a turn failing while the process stays alive is the one thing no hook has been seen to
    // report.
    public TranscriptFold Fold(EnrichmentSnapshot snapshot, IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (Parse(line) is not { } entry)
                continue;

            if (Text(entry, "type") != "assistant")
                continue;

            if (!entry.TryGetProperty("message", out var message)
                || message.ValueKind is not JsonValueKind.Object)
                continue;

            snapshot = Apply(snapshot, message);
        }

        return TranscriptFold.Enrichment(snapshot);
    }

    // Two different questions answered from the same usage block, and conflating them is the trap.
    // **Tokens are cumulative and count only fresh work** — reads off the prompt cache are excluded,
    // because a long session re-reads the same cached prefix on every request and summing those
    // reaches tens of millions, a number that says nothing about the task. **Context is the last
    // request's total**, cache reads included, because that *is* how full the window was — and it
    // has to overwrite rather than accumulate, which is what makes it drop after a compaction.
    //
    // `TurnCount` and `ToolCalls` stay untouched: Claude Code's hooks count both first-hand, and a
    // second producer would fight the first over the same field.
    private static EnrichmentSnapshot Apply(EnrichmentSnapshot snapshot, JsonElement message)
    {
        var usage = message.TryGetProperty("usage", out var found) && found.ValueKind is JsonValueKind.Object
            ? found
            : default;

        var fresh = Number(usage, "input_tokens") + Number(usage, "cache_creation_input_tokens");
        var context = fresh + Number(usage, "cache_read_input_tokens");

        return snapshot with
        {
            ObservedModel = Text(message, "model") ?? snapshot.ObservedModel,
            TokensIn = (snapshot.TokensIn ?? 0) + fresh,
            TokensOut = (snapshot.TokensOut ?? 0) + Number(usage, "output_tokens"),
            ContextUsed = context > 0 ? (int)context : snapshot.ContextUsed,
        };
    }

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

    private static long Number(JsonElement element, string name)
        => element.ValueKind is JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.Number
            && value.TryGetInt64(out var number)
                ? number
                : 0;

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
