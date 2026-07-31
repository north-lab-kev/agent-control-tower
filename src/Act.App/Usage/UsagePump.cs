using Act.Core.Abstractions;
using Act.Infrastructure.Usage;

namespace Act.App.Usage;

public sealed class UsagePump(
    IEnumerable<IUsageProbe> probes,
    UsageState state,
    UsageOptions options,
    ILogger<UsagePump> log) : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();

    public void Start()
    {
        if (!options.Enabled)
            return;

        foreach (var probe in probes)
            _ = PollAsync(probe);
    }

    private async Task PollAsync(IUsageProbe probe)
    {
        using var timer = new PeriodicTimer(options.PollInterval);

        try
        {
            do
            {
                state.Publish(probe.Agent, await probe.ReadAsync(stopping.Token));
            }
            while (await timer.WaitForNextTickAsync(stopping.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            log.LogError(error, "Usage polling for {Agent} stopped.", probe.Agent);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();

        stopping.Dispose();
    }
}
