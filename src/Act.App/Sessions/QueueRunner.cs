using Act.App.Cards;
using Act.App.Hosting;
using Act.App.Settings;
using Act.App.Usage;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Scheduling;
using Act.Infrastructure.Logging;

namespace Act.App.Sessions;

// The unattended half of Ready → Executing. `SessionLauncher` is still the only thing that starts a
// session; this decides *which* cards and *when*, and it decides it with `LaunchQueue` — a pure
// function the board reads too, so a card can never be launched while its strip says `queued`.
//
// It also owns the sleep inhibitor, because keeping the machine awake is a statement about pending
// auto-work and this is the only thing that knows what is pending.
public sealed class QueueRunner(
    BoardState board,
    SessionLauncher launcher,
    UserSettingsService settings,
    UsageState usage,
    TerminalGeometry geometry,
    IWorkingDirectories directories,
    ISleepInhibitor sleep,
    IClock clock,
    ILogger<QueueRunner> log) : IAsyncDisposable
{
    // A backstop, not the mechanism. Every input that can make a card eligible raises an event —
    // a board move, a usage reading, a settings change — except the passage of time itself, which
    // is what a scheduled instant waits for.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    private readonly BackgroundWork work = new(log);

    private QueueEvaluation latest = new([], new Dictionary<Guid, ReadyHold>(), new Dictionary<Guid, DateTimeOffset>());

    // The hold last written to the log per card. A pass runs every 20 seconds and on every board,
    // settings and usage change, so logging the holds each pass would bury everything else in a
    // steady restatement of a board that has not moved — only a hold that *appears or changes* is
    // news. Cleared silently when a card stops being held, because the launch line already says so.
    private readonly Dictionary<Guid, LaunchHold> reportedHolds = [];

    // Raised after every pass. The board reads its hold chips from this runner, and the inputs that
    // change them — the pause switch, the cap, a usage reading — are not card writes, so
    // `BoardState.Changed` never fires for them and a toggled pause would leave every strip saying
    // the opposite of what the queue is now doing.
    public event Action? Evaluated;

    public void Start()
    {
        board.Changed += OnChanged;
        settings.Changed += OnChanged;
        usage.Changed += OnChanged;

        work.StartLoop("The queue runner", Interval, _ => work.RunAsync(PassAsync));
    }

    // Through the same single-flight gate the loop uses: a launch changes the board, which raises
    // `Changed`, which would otherwise re-enter while the pass that caused it is still deciding what
    // else to start.
    private void OnChanged() => work.Request("A queue pass", PassAsync);

    // What the last pass decided. The board renders its hold chips from this rather than evaluating
    // for itself: a pass walks every card and resolves a working directory per candidate pair, and
    // a render happens on every drag-enter and every tick — so asking again per frame cost real
    // time to re-derive an answer that cannot have changed. Every input that *can* change it
    // (a board write, a settings change, a usage reading, the backstop timer) already runs a pass,
    // and `Evaluated` is what tells the board to redraw.
    public QueueEvaluation Latest => latest;

    public QueueEvaluation Evaluate() => latest = LaunchQueue.Evaluate(
        board.All,
        settings.QueuePolicy,
        Usage,
        directories,
        clock.Now);

    private AgentUsage? Usage(AgentType agent)
        => usage.Results.FirstOrDefault(result => result.Agent == agent)?.Usage;

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        var evaluation = Evaluate();

        // Written before anything launches: a relative schedule that has just been resolved must
        // survive a crash on the very next line, or the card is armed against a different window
        // every time ACT starts.
        foreach (var (id, at) in evaluation.Arm)
        {
            if (board.Card(id) is not { } card)
                continue;

            card.EligibleAt = at;

            await board.UpdateAsync(card, cancellationToken);

            using (log.BeginTaskScope(card.Number))
                log.LogInformation("Armed for {At}.", at);
        }

        ReportHolds(evaluation);

        foreach (var card in evaluation.Launch)
        {
            using (log.BeginTaskScope(card.Number))
                log.LogInformation("Launching from the queue.");

            var result = await launcher.LaunchAsync(card, geometry.Last, cancellationToken);

            // Nothing is retried and nothing is moved: a refusal is already recorded on the card by
            // the launcher, and a wait is a hold that will be re-read on the next pass anyway.
            if (!result.Launched)
            {
                using (log.BeginTaskScope(card.Number))
                    log.LogInformation("Did not start: {Message}", result.Message);
            }
        }

        ApplySleep();

        Evaluated?.Invoke();
    }

    private void ReportHolds(QueueEvaluation evaluation)
    {
        foreach (var stale in reportedHolds.Keys.Where(id => !evaluation.Holds.ContainsKey(id)).ToList())
            reportedHolds.Remove(stale);

        foreach (var (id, hold) in evaluation.Holds)
        {
            if (reportedHolds.TryGetValue(id, out var already) && already == hold.Reason)
                continue;

            reportedHolds[id] = hold.Reason;

            if (board.Card(id) is not { } card)
                continue;

            using (log.BeginTaskScope(card.Number))
                Report(hold);
        }
    }

    // One line carrying only the field its reason actually defines — the others are null or zero for
    // every hold that says nothing about them, and a null property renders as the word "null", so a
    // single template for all six would read `until null, 0 of 0 slots in use` most of the time.
    private void Report(ReadyHold hold)
    {
        switch (hold.Reason)
        {
            case LaunchHold.WorkingDir or LaunchHold.Dependency when hold.Blocker is { } blocker:
                log.LogInformation("Held by {Hold}, behind task {Blocker}.", hold.Reason, blocker.Number);
                break;

            case LaunchHold.UsageLimit when hold.Until is { } until:
                log.LogInformation("Held by {Hold} until {Until}.", hold.Reason, until);
                break;

            case LaunchHold.Slot:
                log.LogInformation("Held by {Hold}: {Used} of {Cap} slots in use.", hold.Reason, hold.Used, hold.Cap);
                break;

            default:
                log.LogInformation("Held by {Hold}.", hold.Reason);
                break;
        }
    }

    private void ApplySleep()
    {
        var hold = SleepPolicy.ShouldHold(board.All, settings.KeepAwake, settings.AutoExecutionPaused);

        if (hold == sleep.IsHeld)
            return;

        if (hold)
            sleep.Hold();
        else
            sleep.Release();
    }

    // The hold is released only once every pass has finished, or a pass still in flight could take
    // one back out on its way through `ApplySleep`.
    public async ValueTask DisposeAsync()
    {
        board.Changed -= OnChanged;
        settings.Changed -= OnChanged;
        usage.Changed -= OnChanged;

        await work.DisposeAsync();

        sleep.Release();
    }
}
