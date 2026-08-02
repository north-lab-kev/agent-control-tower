using Act.App.Cards;
using Act.App.Settings;
using Act.App.Usage;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Scheduling;

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

    private readonly CancellationTokenSource stopping = new();

    private readonly SemaphoreSlim gate = new(1, 1);

    private int pending;

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

        _ = LoopAsync();
    }

    private async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            do
            {
                await RunAsync();
            }
            while (await timer.WaitForNextTickAsync(stopping.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "The queue runner stopped.");
        }
    }

    // Coalesced, not queued. A pass launches several cards and arms several more, and every one of
    // those writes raises `Changed` — without this, one pass over five cards would schedule five
    // more passes that each find nothing left to do.
    private void OnChanged()
    {
        if (Interlocked.Exchange(ref pending, 1) == 0)
            _ = RunAsync();
    }

    // Single-flight: a launch changes the board, which raises `Changed`, which would otherwise
    // re-enter here while the pass that caused it is still deciding what else to start.
    private async Task RunAsync()
    {
        try
        {
            await gate.WaitAsync(stopping.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Cleared inside the gate, so a change that lands *during* the pass still schedules the
        // next one — the flag suppresses duplicates, never the news.
        Interlocked.Exchange(ref pending, 0);

        try
        {
            await PassAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "A queue pass failed.");
        }
        finally
        {
            gate.Release();
        }
    }

    public QueueEvaluation Evaluate() => LaunchQueue.Evaluate(
        board.All,
        settings.QueuePolicy,
        Usage,
        directories,
        clock.Now);

    private AgentUsage? Usage(AgentType agent)
        => usage.Results.FirstOrDefault(result => result.Agent == agent)?.Usage;

    private async Task PassAsync()
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

            await board.UpdateAsync(card, stopping.Token);

            log.LogInformation("Card {Number} is armed for {At}.", card.Number, at);
        }

        foreach (var card in evaluation.Launch)
        {
            log.LogInformation("Launching card {Number} from the queue.", card.Number);

            var result = await launcher.LaunchAsync(card, geometry.Last, stopping.Token);

            // Nothing is retried and nothing is moved: a refusal is already recorded on the card by
            // the launcher, and a wait is a hold that will be re-read on the next pass anyway.
            if (!result.Launched)
                log.LogInformation("Card {Number} did not start: {Message}", card.Number, result.Message);
        }

        ApplySleep();

        Evaluated?.Invoke();
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

    public async ValueTask DisposeAsync()
    {
        board.Changed -= OnChanged;
        settings.Changed -= OnChanged;
        usage.Changed -= OnChanged;

        await stopping.CancelAsync();

        sleep.Release();

        stopping.Dispose();
        gate.Dispose();
    }
}
