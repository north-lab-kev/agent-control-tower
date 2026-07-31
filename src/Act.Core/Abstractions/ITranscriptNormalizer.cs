using Act.Core.Events;
using Act.Core.Model;

namespace Act.Core.Abstractions;

// One agent's transcript dialect folded into ACT's vocabulary. It lives with the adapter for the same
// reason the hook normalizer does — the line shape is a fact about that CLI — and it is pure and
// cumulative: the caller owns the running snapshot, so a full re-read and an incremental one differ
// only in which lines arrive.
//
// It returns **both** halves because the two agents need different amounts of it. Claude Code returns
// enrichment only, since its hooks report everything else first-hand. Codex has to return events too
// — activity, turn ends, errors — because its hooks do not fire on the pinned CLI, so its rollout
// file is the only thing that reports them. **Once a CLI build fires Codex hooks, stop emitting those
// events here and let the hooks do it**, exactly as Claude Code already does; the fold then shrinks
// to enrichment and both agents work the same way.
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
