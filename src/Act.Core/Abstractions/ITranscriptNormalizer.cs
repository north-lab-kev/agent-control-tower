using Act.Core.Events;
using Act.Core.Model;

namespace Act.Core.Abstractions;

// One agent's transcript dialect folded into ACT's vocabulary. It lives with the adapter for the same
// reason the hook normalizer does — the line shape is a fact about that CLI — and it is pure and
// cumulative: the caller owns the running snapshot, so a full re-read and an incremental one differ
// only in which lines arrive.
//
// It returns **both** halves, though both agents use it almost the same way: hooks
// report liveness and count the turns and tool calls, and the transcript is enrichment. Codex briefly
// had to return activity and turn ends too, while its hooks were believed dead; that moved to the
// hooks once the real cause was found (ACT quoting the hook command), and the two agents now differ by
// exactly one thing — Codex still raises `TurnFailed`, because a turn that fails while the process
// stays alive is something no hook has been *observed* to report.
public interface ITranscriptNormalizer
{
    AgentType Agent { get; }

    TranscriptFold Fold(EnrichmentSnapshot snapshot, IReadOnlyList<string> lines);
}

// `Events` is what the lines *said happened*; `Snapshot` is what the lines add up to. The distinction
// matters on a catch-up read: replaying an hour of history as events would move a card and count its
// turns twice, so `TranscriptTail` keeps the snapshot and drops the events that first time.
public sealed record TranscriptFold(EnrichmentSnapshot Snapshot, IReadOnlyList<AgentEvent> Events)
{
    public static TranscriptFold Enrichment(EnrichmentSnapshot snapshot) => new(snapshot, []);
}
