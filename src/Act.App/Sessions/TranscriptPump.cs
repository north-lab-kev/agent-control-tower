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
// in every hook payload (`SessionRegistry.TranscriptLocated`). Codex fires no hooks, so its file has to
// be found by convention — see `ITranscriptFinder`, and **delete that path once Codex hooks fire**.
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

    // How long to keep looking for a file the launch should have created. A rollout appears within a
    // second or two; a minute of trying covers a slow start, and giving up leaves a card that still
    // works — it simply reports nothing but what its process can say.
    private static readonly TimeSpan SearchWindow = TimeSpan.FromMinutes(1);

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

        var deadline = clock.Now + SearchWindow;

        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            while (sessions.IsLive(session.TaskId) && clock.Now < deadline)
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
                        sink.Bind(session.TaskId, sessionId);

                    sessions.LocateTranscript(session.TaskId, found.Path);

                    return;
                }

                await timer.WaitForNextTickAsync(stopping.Token);
            }

            log.LogInformation(
                "No transcript found for task {TaskId} within the search window.",
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
