using Act.Agents.ClaudeCode;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.Agents.Tests;

// The nudge that answers an expired token. It goes through the adapter's own query path rather than
// composing a command line of its own, so the measured flag set has exactly one owner — and the run
// it produces is the one `AgentQueryTests` already holds to being cheap. `auth status` was measured
// and rejected here: it reads the credential file without refreshing it. See
// `docs/agent-usage-findings.md`.
public class ClaudeCodeUsageRefresherTests
{
    private static (ClaudeCodeUsageRefresher Refresher, StubCommandHost Commands) Create(
        AgentDefaults? machine = null)
    {
        var commands = new StubCommandHost();

        var adapter = new ClaudeCodeAdapter(
            new StubPtyHost(),
            commands,
            new TestClock(),
            new StubHookEndpoint(),
            new StubAgentConfigFiles(),
            NullLogger<ClaudeCodeAdapter>.Instance);

        return (new ClaudeCodeUsageRefresher(() => adapter, () => machine), commands);
    }

    // Print mode with every customisation off, which is what makes a run ACT asks for on its own
    // account affordable: measured ~5,000 tokens rather than ~30,000.
    [Fact]
    public async Task It_refreshes_through_the_cheap_print_mode_query()
    {
        var (refresher, commands) = Create();

        await refresher.TryRefreshAsync();

        commands.Last.Arguments.Should().Contain("-p");
        commands.Last.Arguments.Should().Contain("--safe-mode");
        commands.Last.Arguments.Should().Contain("--no-session-persistence");
        commands.Last.Arguments[^2].Should().Be("--tools");
        commands.Last.Arguments[^1].Should().BeEmpty();
    }

    // `auth status` is the command this used to run. It exits 0 in under half a second and leaves
    // `expiresAt` exactly where it was, so a nudge built on it reported success and fixed nothing.
    [Fact]
    public async Task It_does_not_rely_on_auth_status_which_does_not_refresh()
    {
        var (refresher, commands) = Create();

        await refresher.TryRefreshAsync();

        commands.Last.Arguments.Should().NotContain("auth");
        commands.Last.Arguments.Should().NotContain("status");
    }

    // Never `auth login` and never `auth logout`: one needs a terminal ACT has not got, and the other
    // would sign the user out of the CLI to fix a meter.
    [Fact]
    public async Task It_never_touches_the_sign_in_state()
    {
        var (refresher, commands) = Create();

        await refresher.TryRefreshAsync();

        commands.Last.Arguments.Should().NotContain("login");
        commands.Last.Arguments.Should().NotContain("logout");
    }

    // Nobody is waiting on this one, unlike a title, so it gets a deadline of its own rather than the
    // query's default — a refresh has a network round trip in it that a cached answer does not.
    [Fact]
    public async Task It_carries_its_own_background_deadline()
    {
        var (refresher, commands) = Create();

        await refresher.TryRefreshAsync();

        commands.Last.Timeout.Should().Be(ClaudeCodeUsageRefresher.Deadline);
    }

    // `CLAUDE_CONFIG_DIR` lives here for the same reason the probe honours it: the credential file the
    // nudge is meant to refresh is the one that variable points at.
    [Fact]
    public async Task It_honours_the_machines_binary_and_environment()
    {
        var machine = new AgentDefaults
        {
            Binary = @"C:\tools\claude.cmd",
            Env = { ["CLAUDE_CONFIG_DIR"] = @"D:\claude" },
        };

        var (refresher, commands) = Create(machine);

        await refresher.TryRefreshAsync();

        commands.Last.Executable.Should().Be(@"C:\tools\claude.cmd");
        commands.Last.Environment.Should().Contain(
            new KeyValuePair<string, string>("CLAUDE_CONFIG_DIR", @"D:\claude"));
    }

    [Fact]
    public async Task A_run_that_answered_reports_the_nudge_as_delivered()
    {
        var (refresher, commands) = Create();

        commands.Result = new CommandResult(0, "ok", string.Empty);

        (await refresher.TryRefreshAsync()).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task A_run_that_failed_or_timed_out_reports_nothing_delivered(int exitCode, bool timedOut)
    {
        var (refresher, commands) = Create();

        commands.Result = new CommandResult(exitCode, string.Empty, "not logged in", timedOut);

        (await refresher.TryRefreshAsync()).Should().BeFalse();
    }

    // A missing CLI throws at spawn, and a background poll is the last place that should surface as an
    // unhandled exception: the meter has an `unavailable` state that already says this.
    [Fact]
    public async Task A_CLI_that_cannot_be_started_is_not_an_exception_the_pump_has_to_catch()
    {
        var (refresher, commands) = Create();

        commands.Fails = new System.ComponentModel.Win32Exception(2);

        (await refresher.TryRefreshAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task The_callers_own_cancellation_still_propagates()
    {
        var (refresher, commands) = Create();

        commands.Fails = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresher.TryRefreshAsync());
    }
}
