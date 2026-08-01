using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.Usage;

namespace Act.App.Usage;

public sealed class UsagePump(
    IEnumerable<IUsageProbe> probes,
    UsageState state,
    UsageOptions options,
    UserSettingsService settings,
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
        var failures = 0;
        var reported = (UsageAvailability?)null;

        try
        {
            while (true)
            {
                var wait = options.PollInterval;

                // Checked every pass rather than once at startup, so switching an agent off stops the
                // polling immediately and switching it back on resumes it — without a restart, and
                // without a second timer to manage.
                if (settings.EnabledAgents.Contains(probe.Agent))
                {
                    var result = await probe.ReadAsync(stopping.Token);

                    state.Publish(result);

                    failures = UsageBackoff.Count(result.Availability, failures);
                    wait = UsageBackoff.Delay(options.PollInterval, failures);

                    if (result.Availability != reported)
                    {
                        reported = result.Availability;

                        log.LogInformation(
                            "{Agent} usage is {Availability}; next check in {Wait}.",
                            probe.Agent,
                            result.Availability,
                            wait);
                    }
                }

                await Task.Delay(wait, stopping.Token);
            }
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
