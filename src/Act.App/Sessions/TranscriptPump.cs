using Act.App.Cards;
using Act.App.Hosting;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.App.Sessions;

// The other half of ingestion, beside the hook endpoint: what only the transcript knows — tokens,
// context, the model actually running and the last thing said. One loop per live session, from the
// moment the file is known until the session ends.
//
// Polled rather than watched, deliberately. A `FileSystemWatcher` on an appended file needs its own
// debounce and reports late anyway; a read from a stored offset once a second is a few kilobytes.
//
// **One way a session's file becomes known, for both agents: the hooks say so.** There is no
// fallback that guesses at it — see `docs/design-notes.md` for the one that was deleted and the
// cost that acceptance carries for a user who declines Codex's hook-review screen.
public sealed class TranscriptPump(
    SessionRegistry sessions,
    BoardState board,
    IAgentEventSink sink,
    IAgentCapabilityCatalog capabilities,
    IEnumerable<ITranscriptNormalizer> normalizers,
    ITranscriptReader reader,
    IClock clock,
    ILogger<TranscriptPump> log) : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<AgentType, ITranscriptNormalizer> byAgent =
        normalizers.ToDictionary(normalizer => normalizer.Agent);

    private readonly BackgroundWork work = new(log);

    public void Start() => sessions.TranscriptLocated += Attach;

    private void Attach(IAgentSession session, string path)
        => work.Start($"Transcript tail for task {session.TaskId}", token => TailAsync(session, path, token));

    // Its own timer rather than `StartLoop`, because the loop ends on the session going away as well
    // as on shutdown — and one tail per session must not be serialized against the others.
    //
    // An agent with no transcript normalizer is not an error: it simply reports what its hooks and its
    // process can say.
    private async Task TailAsync(IAgentSession session, string path, CancellationToken cancellationToken)
    {
        if (Agent(session) is not { } agent || !byAgent.TryGetValue(agent, out var normalizer))
            return;

        var tail = new TranscriptTail(reader, normalizer, capabilities.For(agent), path);

        using var timer = new PeriodicTimer(PollInterval);

        // Read before the first wait: the file already holds the whole session, and on a restore
        // that is a session with an hour of numbers in it.
        do
        {
            Poll(session, tail);
        }
        while (sessions.IsLive(session.TaskId) && await timer.WaitForNextTickAsync(cancellationToken));
    }

    // A read that throws must not end the tail: the CLI owns this file and a mid-write moment is a
    // transient, so the next tick tries again from the same offset.
    private void Poll(IAgentSession session, TranscriptTail tail)
    {
        try
        {
            var update = tail.Advance();

            if (update.IsNothing)
                return;

            foreach (var observed in update.Events)
                sink.Publish(session.TaskId, observed);

            // After the events, because its counts are absolute and settle any disagreement with what
            // the projection just incremented.
            if (update.Snapshot is { } snapshot)
                sink.Publish(
                    session.TaskId,
                    new SessionEnriched(session.SessionId ?? string.Empty, clock.Now, snapshot));
        }
        catch (IOException error)
        {
            log.LogDebug(error, "Transcript read for task {TaskId} was skipped.", session.TaskId);
        }
        catch (UnauthorizedAccessException error)
        {
            log.LogDebug(error, "Transcript read for task {TaskId} was skipped.", session.TaskId);
        }
    }

    private AgentType? Agent(IAgentSession session) => board.Card(session.TaskId)?.AgentType;

    public async ValueTask DisposeAsync()
    {
        sessions.TranscriptLocated -= Attach;

        await work.DisposeAsync();
    }
}
