using Act.App.Settings;
using Act.App.Usage;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.Infrastructure.Usage;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// One poll loop per probe. What the loop owes the rest of ACT: a reading reaches `UsageState`, an
// agent switched off has its reading *withdrawn* rather than left to go stale, and an expired token
// is nudged rather than reported as a dead end.
public class UsagePumpTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSettingsStore store = new();

    private readonly UsageState state = new();

    private readonly FakeUsageProbe claude = new(AgentType.ClaudeCode);

    [Fact]
    public async Task A_reading_reaches_the_state()
    {
        claude.Answers = Available(40);

        await using var pump = Pump(Enabled(AgentType.ClaudeCode));

        pump.Start();

        await Until(() => state.Results.Count == 1);

        var reading = state.Results.Single();

        reading.Agent.Should().Be(AgentType.ClaudeCode);
        reading.IsAvailable.Should().BeTrue();
        reading.Usage!.Of(UsageWindowKind.Session)!.Percent.Should().Be(40);
    }

    // The deployment switch, not a preference: a machine with no outbound network turns the feature
    // off and nothing should be asked at all.
    [Fact]
    public async Task Nothing_is_polled_when_the_feature_is_off()
    {
        await using var pump = Pump(Enabled(AgentType.ClaudeCode), options: new UsageOptions { Enabled = false });

        pump.Start();

        await Task.Delay(100);

        claude.Reads.Should().Be(0);
        state.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task An_agent_that_is_not_switched_on_is_not_asked()
    {
        await using var pump = Pump(Settings());

        pump.Start();

        await Task.Delay(100);

        claude.Reads.Should().Be(0);
    }

    // A reading nobody withdraws is one the state keeps for as long as ACT runs, so switching the
    // agent back on would answer with an hour-old percentage for a window that has since rolled over.
    [Fact]
    public async Task Switching_an_agent_off_withdraws_its_reading_rather_than_leaving_it_to_go_stale()
    {
        var settings = Settings();

        state.Publish(Available(40));
        state.Results.Should().ContainSingle();

        await using var pump = Pump(settings);

        pump.Start();

        await Until(() => state.Results.Count == 0);
    }

    [Fact]
    public async Task Every_registered_probe_gets_a_loop_of_its_own()
    {
        var codex = new FakeUsageProbe(AgentType.Codex) { Answers = Available(10, AgentType.Codex) };

        claude.Answers = Available(20);

        await using var pump = Pump(
            Enabled(AgentType.ClaudeCode, AgentType.Codex),
            probes: [claude, codex]);

        pump.Start();

        await Until(() => state.Results.Count == 2);

        state.Results.Select(result => result.Agent)
            .Should().Equal(AgentType.ClaudeCode, AgentType.Codex);
    }

    // The CLI owns the credential file and renews it on use, so the fix is to make the CLI run —
    // never to perform the OAuth exchange here.
    [Fact]
    public async Task An_expired_token_is_nudged_and_then_read_again()
    {
        claude.Answers = Unavailable(UsageAvailability.Expired);
        claude.Then = Available(55);

        var refresher = new FakeUsageRefresher(AgentType.ClaudeCode) { Delivers = true };

        await using var pump = Pump(Enabled(AgentType.ClaudeCode), refreshers: [refresher]);

        pump.Start();

        await Until(() => state.Results.Any(result => result.IsAvailable));

        refresher.Attempts.Should().Be(1);
        claude.Reads.Should().Be(2, "the second reading is the point of the nudge");
    }

    // A CLI ACT could not start changed nothing, and the endpoint is undocumented enough that a
    // request which cannot tell us anything new should not be made.
    [Fact]
    public async Task A_nudge_that_could_not_be_delivered_is_not_followed_by_a_second_read()
    {
        claude.Answers = Unavailable(UsageAvailability.Expired);

        var refresher = new FakeUsageRefresher(AgentType.ClaudeCode) { Delivers = false };

        await using var pump = Pump(Enabled(AgentType.ClaudeCode), refreshers: [refresher]);

        pump.Start();

        await Until(() => refresher.Attempts == 1);
        await Until(() => state.Results.Count == 1);

        claude.Reads.Should().Be(1);
    }

    [Fact]
    public async Task The_nudge_can_be_turned_off_without_turning_the_polling_off()
    {
        claude.Answers = Unavailable(UsageAvailability.Expired);

        var refresher = new FakeUsageRefresher(AgentType.ClaudeCode) { Delivers = true };

        await using var pump = Pump(
            Enabled(AgentType.ClaudeCode),
            options: new UsageOptions { RefreshOnExpiry = false },
            refreshers: [refresher]);

        pump.Start();

        await Until(() => state.Results.Count == 1);

        refresher.Attempts.Should().Be(0);
        state.Results.Single().Availability.Should().Be(UsageAvailability.Expired);
    }

    // Only `Expired` resolves itself by running the CLI. `SignInRequired` is the same situation after
    // the refresh token's own clock ran out, and no amount of nudging fixes it.
    [Theory]
    [InlineData(UsageAvailability.NotSignedIn)]
    [InlineData(UsageAvailability.SignInRequired)]
    [InlineData(UsageAvailability.Unreachable)]
    public async Task An_outcome_the_nudge_cannot_answer_is_reported_as_it_is(UsageAvailability availability)
    {
        claude.Answers = Unavailable(availability);

        var refresher = new FakeUsageRefresher(AgentType.ClaudeCode) { Delivers = true };

        await using var pump = Pump(Enabled(AgentType.ClaudeCode), refreshers: [refresher]);

        pump.Start();

        await Until(() => state.Results.Count == 1);

        refresher.Attempts.Should().Be(0);
        state.Results.Single().Availability.Should().Be(availability);
    }

    [Fact]
    public async Task An_expired_token_with_no_refresher_for_that_agent_is_simply_reported()
    {
        claude.Answers = Unavailable(UsageAvailability.Expired);

        var refresher = new FakeUsageRefresher(AgentType.Codex) { Delivers = true };

        await using var pump = Pump(Enabled(AgentType.ClaudeCode), refreshers: [refresher]);

        pump.Start();

        await Until(() => state.Results.Count == 1);

        refresher.Attempts.Should().Be(0);
        claude.Reads.Should().Be(1);
    }

    [Fact]
    public async Task Disposing_stops_the_loop()
    {
        claude.Answers = Available(40);

        var pump = Pump(Enabled(AgentType.ClaudeCode));

        pump.Start();

        await Until(() => claude.Reads >= 1);

        await pump.DisposeAsync();

        var reads = claude.Reads;

        await Task.Delay(100);

        claude.Reads.Should().Be(reads);
    }

    [Fact]
    public async Task Disposing_a_pump_that_never_started_is_harmless()
    {
        var pump = Pump(Enabled(AgentType.ClaudeCode));

        var dispose = async () => await pump.DisposeAsync();

        await dispose.Should().NotThrowAsync();
    }

    private UsagePump Pump(
        UserSettingsService settings,
        UsageOptions? options = null,
        IUsageProbe[]? probes = null,
        IUsageRefresher[]? refreshers = null)
        => new(
            probes ?? [claude],
            refreshers ?? [],
            state,
            options ?? new UsageOptions(),
            settings,
            new FrozenClock(Now),
            NullLogger<UsagePump>.Instance);

    private UserSettingsService Settings() => new(store, new AppCulture());

    private UserSettingsService Enabled(params AgentType[] agents)
    {
        var settings = Settings();

        foreach (var agent in agents)
        {
            var defaults = settings.Defaults(agent);
            defaults.Enabled = true;
            settings.SetDefaults(defaults);
        }

        return settings;
    }

    private static UsageProbeResult Available(int percent, AgentType agent = AgentType.ClaudeCode)
        => UsageProbeResult.Of(new AgentUsage(
            agent,
            [new UsageWindow(UsageWindowKind.Session, percent, Now.AddHours(3))],
            Now,
            LimitReached: false,
            Plan: "pro"));

    private static UsageProbeResult Unavailable(
        UsageAvailability availability,
        AgentType agent = AgentType.ClaudeCode)
        => UsageProbeResult.Unavailable(agent, availability, Now);

    // A bounded wait rather than a fixed delay: the loop is real background work and a sleep long
    // enough to be safe would be long enough to be the slowest test in the suite.
    private static async Task Until(Func<bool> settled)
    {
        for (var waited = 0; waited < 500; waited++)
        {
            if (settled())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The pump never reached the expected state.");
    }

    private sealed class FakeUsageProbe(AgentType agent) : IUsageProbe
    {
        public AgentType Agent => agent;

        // What the first read answers, and what every read after it answers — a poll loop reads
        // forever, so a queue of one would run dry mid-test.
        public UsageProbeResult Answers { get; set; } = UsageProbeResult.Unavailable(agent, UsageAvailability.Failed, Now);

        public UsageProbeResult? Then { get; set; }

        public int Reads { get; private set; }

        public Task<UsageProbeResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;

            return Task.FromResult(Reads == 1 || Then is null ? Answers : Then);
        }
    }

    private sealed class FakeUsageRefresher(AgentType agent) : IUsageRefresher
    {
        public AgentType Agent => agent;

        public bool Delivers { get; set; }

        public int Attempts { get; private set; }

        public Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
        {
            Attempts++;

            return Task.FromResult(Delivers);
        }
    }
}
