using Act.App.Settings;

namespace Act.App.Cards;

public sealed class RetentionPump(
    BoardState board,
    UserSettingsService settings,
    ILogger<RetentionPump> log) : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly CancellationTokenSource stopping = new();

    private readonly SemaphoreSlim gate = new(1, 1);

    public void Start()
    {
        settings.Changed += OnSettingsChanged;

        _ = SweepLoopAsync();
    }

    private async Task SweepLoopAsync()
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            do
            {
                await SweepAsync();
            }
            while (await timer.WaitForNextTickAsync(stopping.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "Completed-card retention stopped.");
        }
    }

    private void OnSettingsChanged() => _ = SweepAsync();

    private async Task SweepAsync()
    {
        try
        {
            await gate.WaitAsync(stopping.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await board.ApplyRetentionAsync(settings.CompletedRetentionWindow, stopping.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "Completed-card retention sweep failed.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        settings.Changed -= OnSettingsChanged;

        await stopping.CancelAsync();

        stopping.Dispose();
        gate.Dispose();
    }
}
