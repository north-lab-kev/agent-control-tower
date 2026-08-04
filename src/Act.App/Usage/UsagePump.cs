using Act.App.Hosting;
using Act.App.Settings;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Infrastructure.Usage;

namespace Act.App.Usage;

public sealed class UsagePump(
    IEnumerable<IUsageProbe> probes,
    IEnumerable<IUsageRefresher> refreshers,
    UsageState state,
    UsageOptions options,
    UserSettingsService settings,
    IClock clock,
    ILogger<UsagePump> log) : IAsyncDisposable
{
    private readonly BackgroundWork work = new(log);

    private readonly IReadOnlyDictionary<AgentType, IUsageRefresher> byAgent =
        refreshers.ToDictionary(refresher => refresher.Agent);

    public void Start()
    {
        if (!options.Enabled)
            return;

        foreach (var probe in probes)
            work.Start($"Usage polling for {probe.Agent}", token => PollAsync(probe, token));
    }

    // Its own loop rather than `StartLoop`, because the wait between passes is the answer to the last
    // one — `UsageBackoff` lengthens it after a failure that actually reached the network.
    private async Task PollAsync(IUsageProbe probe, CancellationToken cancellationToken)
    {
        var failures = 0;
        var reported = (UsageAvailability?)null;
        var nudged = (DateTimeOffset?)null;
        var nudgeFailures = 0;

        while (true)
        {
            var wait = options.PollInterval;

            // Checked every pass rather than once at startup, so switching an agent off stops the
            // polling immediately and switching it back on resumes it — without a restart, and
            // without a second timer to manage.
            if (settings.EnabledAgents.Contains(probe.Agent))
            {
                var result = await probe.ReadAsync(cancellationToken);

                // An expired token is the one unavailable outcome ACT can act on: the CLI owns the
                // file and refreshes it on use, so the fix is to make the CLI run — never to perform
                // the OAuth exchange here, which would race a rotating refresh token and could cost
                // the user their login. The nudge is a measured ~5,000 tokens (see
                // `docs/agent-usage-findings.md`), so it is rate-limited on its own clock rather than
                // the poll interval, and that clock lengthens after a nudge that changed nothing.
                if (options.RefreshOnExpiry
                    && UsageRefresh.Answers(result.Availability)
                    && UsageRefresh.Due(
                        clock.Now,
                        nudged,
                        UsageRefresh.Delay(options.RefreshCooldown, nudgeFailures))
                    && byAgent.TryGetValue(probe.Agent, out var refresher))
                {
                    nudged = clock.Now;

                    var delivered = await refresher.TryRefreshAsync(cancellationToken);

                    // Only worth a second reading if the nudge actually ran: a CLI ACT could not
                    // start changed nothing, and the endpoint is undocumented enough that a request
                    // which cannot tell us anything new should not be made.
                    if (delivered)
                        result = await probe.ReadAsync(cancellationToken);

                    nudgeFailures = UsageRefresh.Count(result.Availability, nudgeFailures);

                    log.LogInformation(
                        "{Agent} usage token had expired; the refresh nudge {Outcome} and usage now reads {Availability}. Next nudge no sooner than {Cooldown}.",
                        probe.Agent,
                        delivered ? "ran" : "could not be delivered",
                        result.Availability,
                        UsageRefresh.Delay(options.RefreshCooldown, nudgeFailures));
                }

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
            else
            {
                // Said rather than left unsaid. Skipping the probe stops the polling, but a
                // reading nobody withdraws is one the state keeps for as long as ACT runs —
                // so switching the agent back on would answer with an hour-old percentage for
                // a window that has since rolled over. `Off` is what empties it, and the
                // failure count goes with it: a re-enabled agent is asked again immediately,
                // not through a backoff it earned before it was switched off.
                state.Publish(UsageProbeResult.Unavailable(probe.Agent, UsageAvailability.Off, clock.Now));

                failures = 0;
                reported = null;
                nudged = null;
                nudgeFailures = 0;
            }

            await Task.Delay(wait, cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => work.DisposeAsync();
}
