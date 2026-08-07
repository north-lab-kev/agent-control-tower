using Act.Core.Abstractions;
using Act.Core.Events;

namespace Act.Core.Agents;

// The stateful half of transcript ingestion: where the last read stopped, and what was known then.
// The first read takes the whole file — a restart or a resume must not report a session that has
// been working for an hour as if it had just started — and every read after it takes only the tail.
public sealed class TranscriptTail(
    ITranscriptReader reader,
    ITranscriptNormalizer normalizer,
    AgentCapabilities capabilities,
    string path)
{
    private EnrichmentSnapshot snapshot = new();

    private long offset;

    private bool caughtUp;

    public TranscriptUpdate Advance()
    {
        var read = reader.Read(path, offset);

        offset = read.Offset;

        if (read.Lines.Count is 0)
            return TranscriptUpdate.Nothing;

        var fold = normalizer.Fold(snapshot, read.Lines);
        var folded = WithContextLimit(fold.Snapshot);

        // The catch-up read is the whole file, so its events are history rather than news: replaying
        // them would move the card on a turn that ended an hour ago and count every turn a second
        // time. The snapshot is safe because it is absolute — it says where the session *is*.
        var events = caughtUp ? fold.Events : [];

        caughtUp = true;

        // Null when nothing changed, which is most polls: republishing an identical snapshot would
        // mark the card dirty and re-render the board for news that is not news.
        if (folded == snapshot)
            return new TranscriptUpdate(null, events);

        snapshot = folded;

        return new TranscriptUpdate(folded, events);
    }

    // No transcript carries the limit for Claude Code — it is a fact about the model, so it comes from
    // the adapter's capability list, matched against the model the session reports *actually* running.
    // An unknown model leaves it null, and the card shows no percentage rather than a wrong one. Codex
    // reports its own window in the transcript, so a fold that already found one is left alone.
    private EnrichmentSnapshot WithContextLimit(EnrichmentSnapshot folded)
        => folded.ContextLimit is null && capabilities.ContextLimitFor(folded.ObservedModel) is { } limit
            ? folded with { ContextLimit = limit }
            : folded;
}

// Ordered on purpose where both are present: the events are what happened, and the snapshot is the
// correction that follows — its counts are absolute, so it settles any disagreement with the
// projection's own incrementing.
public sealed record TranscriptUpdate(EnrichmentSnapshot? Snapshot, IReadOnlyList<AgentEvent> Events)
{
    public static readonly TranscriptUpdate Nothing = new(null, []);

    public bool IsNothing => Snapshot is null && Events.Count is 0;
}
