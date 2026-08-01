using Act.App.Cards;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.App.Sessions;

// The other half of ingestion, beside the hook endpoint: what only the transcript knows — tokens,
// context, the model actually running, the last thing said, and for Codex the turn boundaries too.
// One loop per live session, from the moment the file is known until the session ends.
//
// Polled rather than watched, deliberately. A `FileSystemWatcher` on an appended file needs its own
// debounce and reports late anyway; a read from a stored offset once a second is a few kilobytes.
//
// Two ways a session's file becomes known, and the second one is a workaround. Claude Code *tells* ACT
// in every hook payload (`SessionRegistry.TranscriptLocated`). Codex's hooks do fire as of
// 2026-07-31, but no payload has yet been seen to arrive, so its file is still found by convention —
// see `ITranscriptFinder`, and **delete that path once a payload names the file**.
public sealed class TranscriptPump(
    SessionRegistry sessions,
    BoardState board,
    IAgentEventSink sink,
    IAgentCapabilityCatalog capabilities,
    IEnumerable<ITranscriptNormalizer> normalizers,
    IEnumerable<ITranscriptFinder> finders,
    ITranscriptReader reader,
    IClock clock,
    ILogger<TranscriptPump> log) : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<AgentType, ITranscriptNormalizer> byAgent =
        normalizers.ToDictionary(normalizer => normalizer.Agent);

    private readonly Dictionary<AgentType, ITranscriptFinder> findersByAgent =
        finders.ToDictionary(finder => finder.Agent);

    private readonly CancellationTokenSource stopping = new();

    public void Start()
    {
        sessions.Added += OnAdded;
        sessions.TranscriptLocated += Attach;
    }

    // Only the agents nobody tells ACT about: a Claude Code session's first hook names its file, so
    // searching for it would be a race against the answer.
    private void OnAdded(IAgentSession session)
    {
        if (Agent(session) is { } agent && findersByAgent.ContainsKey(agent))
            _ = SearchAsync(session);
    }

    private void Attach(IAgentSession session, string path) => _ = TailAsync(session, path);

    private async Task SearchAsync(IAgentSession session)
    {
        if (Agent(session) is not { } agent || !findersByAgent.TryGetValue(agent, out var finder))
            return;

        // `clock.Now`, not the card's `LaunchedAt`: that field is stamped on a card's first launch and
        // never again, so on a relaunch it is stale and every transcript written in between looks like
        // a candidate — which is exactly how a card once bound itself to a session from an hour
        // earlier. This runs the moment the session is registered, so it is the spawn to within a tick.
        var search = new TranscriptSearch(
            board.Card(session.TaskId)?.WorkingDir ?? string.Empty,
            clock.Now,
            sessions.ClaimedTranscripts,
            session.SessionId);

        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            // For as long as the session lives, with no deadline. A rollout normally appears within a
            // second or two, but it is written when the *session* starts — and Codex parks on its
            // pre-session gates first, so the wait is however long the user takes to answer them. A
            // one-minute window expired while a hook-review screen was still up, and the card then
            // ran a whole turn unbound and reported nothing. Measured 2026-07-31.
            while (sessions.IsLive(session.TaskId))
            {
                // The id can arrive while the search is running — a restore binds it late — and an
                // exact-id match beats the guessing, so it is re-read on every tick.
                var attempt = search with
                {
                    Claimed = sessions.ClaimedTranscripts,
                    SessionId = session.SessionId ?? search.SessionId,
                };

                if (finder.Locate(attempt) is { } found)
                {
                    // The binding and the file arrive together for Codex, and this is the only place
                    // either can come from: there is no id to pre-mint and no payload to read.
                    if (found.SessionId is { Length: > 0 } sessionId)
                    {
                        sink.Bind(session.TaskId, sessionId);

                        await PersistSessionIdAsync(session.TaskId, sessionId);
                    }

                    sessions.LocateTranscript(session.TaskId, found.Path);

                    return;
                }

                await timer.WaitForNextTickAsync(stopping.Token);
            }

            log.LogInformation(
                "Task {TaskId} ended without a transcript ever being found.",
                session.TaskId);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "Transcript search for task {TaskId} stopped.", session.TaskId);
        }
    }

    // `sink.Bind` reaches the live session only, and a session object dies with the process. For an
    // agent whose id ACT cannot pre-mint, the card is the only place the binding can survive — without
    // this the rail reads "reported at session start" for the whole session, and a restore can never
    // match its rollout by identity.
    private async Task PersistSessionIdAsync(Guid taskId, string sessionId)
    {
        if (board.Card(taskId) is not { } card || card.SessionId == sessionId)
            return;

        card.SessionId = sessionId;

        await board.UpdateAsync(card);
    }

    // An agent with no transcript normalizer is not an error: it simply reports what its hooks and its
    // process can say.
    private async Task TailAsync(IAgentSession session, string path)
    {
        if (Agent(session) is not { } agent || !byAgent.TryGetValue(agent, out var normalizer))
            return;

        var tail = new TranscriptTail(reader, normalizer, capabilities.For(agent), path);

        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            // Read before the first wait: the file already holds the whole session, and on a restore
            // that is a session with an hour of numbers in it.
            do
            {
                Poll(session, tail);
            }
            while (sessions.IsLive(session.TaskId) && await timer.WaitForNextTickAsync(stopping.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "Transcript tail for task {TaskId} stopped.", session.TaskId);
        }
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
        sessions.Added -= OnAdded;
        sessions.TranscriptLocated -= Attach;

        await stopping.CancelAsync();

        stopping.Dispose();
    }
}
