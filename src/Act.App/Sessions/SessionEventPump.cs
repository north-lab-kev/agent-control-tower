using Act.App.Cards;
using Act.App.Hosting;
using Act.App.Notifications;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.Logging;

namespace Act.App.Sessions;

// Where the observed stream meets the board. One drain loop per live session, and all the thinking
// is elsewhere: `MetricsProjection` decides what the numbers become, `RulesEngine` decides what the
// column and badge become, and this only sequences them and persists the result.
//
// Writes are split by urgency on purpose. A move is user-visible and lands immediately; metrics
// alone are debounced and flushed as one batched write, because every board write re-reads every
// card and re-renders the whole board, and a chatty session emits tool events several times a
// second.
public sealed class SessionEventPump(
    SessionRegistry sessions,
    BoardState board,
    NotificationDispatcher notifications,
    IClock clock,
    ILogger<SessionEventPump> log) : IAsyncDisposable
{
    private static readonly TimeSpan MetricsFlushInterval = TimeSpan.FromSeconds(1);

    private readonly BackgroundWork work = new(log);

    private readonly Lock gate = new();

    // The card *instance* the projection last wrote to, not just its id. `BoardState` replaces every
    // instance on each reload, so by the time the flush runs the board's copy may be a different
    // object that never saw the increments — see `FlushAsync`.
    private readonly Dictionary<Guid, Card> dirty = [];

    public void Start()
    {
        sessions.Added += Attach;

        work.StartLoop("The metrics flush", MetricsFlushInterval, FlushAsync);
    }

    // One drain per session, named after the card so a failure says which one went deaf: the session
    // and its terminal are unaffected by a pump that dies, so it is reported rather than rethrown.
    private void Attach(IAgentSession session)
        => work.Start($"Event pump for task {session.TaskId}", token => DrainAsync(session, token));

    private async Task DrainAsync(IAgentSession session, CancellationToken cancellationToken)
    {
        await foreach (var observed in session.Events.WithCancellation(cancellationToken))
            await ApplyAsync(session.TaskId, observed, cancellationToken);

        // The stream ends when the agent's process does, and nothing else notices: a session whose
        // CLI exited on its own would otherwise stay in the registry forever, which reads as *live*
        // — so the card could not be retried (`CardRetry` asks exactly that), could not be
        // relaunched, opened onto a frozen terminal, and its pty and hook token were never
        // released. Matched on the instance, so a restart's replacement is left alone.
        await sessions.EndAsync(session);
    }

    private async Task ApplyAsync(
        Guid taskId,
        Core.Events.AgentEvent observed,
        CancellationToken cancellationToken)
    {
        // A card can be deleted while its session is still running its last turn out.
        if (board.Card(taskId) is not { } card)
            return;

        var touchedMetrics = MetricsProjection.Apply(card, observed);
        var move = RulesEngine.Decide(card, observed);

        if (move is not null && move.ChangesAnything(card))
        {
            using (log.BeginTaskScope(card.Number, card.SessionId))
            {
                log.LogInformation(
                    "{From} → {To} ({Badge}), because {Reason}.",
                    card.Column,
                    move.Column,
                    move.Badge,
                    move.Reason);

                if (move.Detail is { } detail)
                    log.LogDebug("What the move reported: {Detail}", detail);
            }

            card.Column = move.Column;
            card.Badge = move.Badge;

            card.Transitions.Add(new Transition
            {
                At = clock.Now,
                Column = move.Column,
                Badge = move.Badge,
                Reason = move.Reason,
                Note = move.Detail,
            });

            await board.UpdateAsync(card, cancellationToken);

            notifications.Notify(card);

            return;
        }

        if (touchedMetrics)
        {
            lock (gate)
                dirty[taskId] = card;
        }
    }

    // The projected instance carries the numbers; the board's carries everything else. Any write in
    // the meantime — another card's turn ending, a title edited on the task page — reloads the board
    // and replaces every instance, so writing back the projected one would clobber whatever else
    // changed, and writing back the board's one would silently drop a second of tool calls and leave
    // `lastActivityAt` behind, which is what makes a working card start claiming it has gone quiet.
    // So the two fields the projection owns are carried across — onto the instance the board holds
    // *inside* the write gate — and every dirty card lands in the one batched write, so a flush
    // costs one reload and one re-render however many sessions are talking.
    private Task FlushAsync(CancellationToken cancellationToken)
    {
        Dictionary<Guid, Card> pending;

        lock (gate)
        {
            if (dirty.Count == 0)
                return Task.CompletedTask;

            pending = new Dictionary<Guid, Card>(dirty);

            dirty.Clear();
        }

        return board.UpdateManyAsync(
            [.. pending.Keys],
            card =>
            {
                var projected = pending[card.Id];

                if (ReferenceEquals(card, projected))
                    return;

                card.Metrics = projected.Metrics;
                card.ObservedModel = projected.ObservedModel;
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        sessions.Added -= Attach;

        await work.DisposeAsync();
    }
}
