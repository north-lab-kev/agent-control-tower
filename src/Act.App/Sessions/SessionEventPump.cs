using Act.App.Cards;
using Act.App.Notifications;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;

namespace Act.App.Sessions;

// Where the observed stream meets the board. One drain loop per live session, and all the thinking
// is elsewhere: `MetricsProjection` decides what the numbers become, `RulesEngine` decides what the
// column and badge become, and this only sequences them and persists the result.
//
// Writes are split by urgency on purpose. A move is user-visible and lands immediately; metrics
// alone are debounced, because `BoardState.UpdateAsync` re-reads every card and re-renders the whole
// board, and a chatty session emits tool events several times a second.
public sealed class SessionEventPump(
    SessionRegistry sessions,
    BoardState board,
    NotificationDispatcher notifications,
    IClock clock,
    ILogger<SessionEventPump> log) : IAsyncDisposable
{
    private static readonly TimeSpan MetricsFlushInterval = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource stopping = new();

    private readonly Lock gate = new();

    private readonly HashSet<Guid> dirty = [];

    private Task? flushing;

    public void Start()
    {
        sessions.Added += Attach;
        flushing = FlushLoopAsync();
    }

    private void Attach(IAgentSession session) => _ = DrainAsync(session);

    private async Task DrainAsync(IAgentSession session)
    {
        try
        {
            await foreach (var observed in session.Events.WithCancellation(stopping.Token))
                await ApplyAsync(session.TaskId, observed);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            // A pump that dies takes the board's liveness with it and says nothing. The session and
            // its terminal are unaffected, so this is reported rather than rethrown.
            log.LogError(error, "Event pump for task {TaskId} stopped.", session.TaskId);
        }
    }

    private async Task ApplyAsync(Guid taskId, Core.Events.AgentEvent observed)
    {
        // A card can be deleted while its session is still running its last turn out.
        if (board.Card(taskId) is not { } card)
            return;

        var touchedMetrics = MetricsProjection.Apply(card, observed);
        var move = RulesEngine.Decide(card, observed);

        if (move is not null && move.ChangesAnything(card))
        {
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

            await board.UpdateAsync(card, stopping.Token);

            notifications.Notify(card);

            return;
        }

        if (touchedMetrics)
        {
            lock (gate)
                dirty.Add(taskId);
        }
    }

    private async Task FlushLoopAsync()
    {
        using var timer = new PeriodicTimer(MetricsFlushInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stopping.Token))
                await FlushAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FlushAsync()
    {
        Guid[] pending;

        lock (gate)
        {
            if (dirty.Count == 0)
                return;

            pending = [.. dirty];

            dirty.Clear();
        }

        foreach (var taskId in pending)
        {
            if (board.Card(taskId) is { } card)
                await board.UpdateAsync(card, stopping.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        sessions.Added -= Attach;

        await stopping.CancelAsync();

        if (flushing is not null)
            await flushing;

        stopping.Dispose();
    }
}
