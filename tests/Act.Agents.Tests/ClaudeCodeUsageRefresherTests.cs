using Act.Agents.ClaudeCode;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// The nudge that answers an expired token, held to the property that makes it safe to put on a timer:
// it asks the CLI about its own login and nothing else. The measured behaviour is in
// `docs/agent-usage-findings.md`.
public class ClaudeCodeUsageRefresherTests
{
    private static (ClaudeCodeUsageRefresher Refresher, StubCommandHost Commands) Create(
        AgentDefaults? machine = null)
    {
        var commands = new StubCommandHost();

        return (
            new ClaudeCodeUsageRefresher(commands, new StubAgentConfigFiles(), () => machine),
            commands);
    }

    // The whole reason this is allowed to run unattended: `auth status` reads the credential file and
    // makes no model call, so a nudge on a timer cannot spend the user's quota. A prompt anywhere in
    // here would make it billable.
    [Fact]
    public async Task It_asks_the_CLI_about_its_own_login_and_nothing_else()
    {
        var (refresher, commands) = Create();

        await refresher.TryRefreshAsync();

        commands.Last.Arguments.Should().Equal("auth", "status", "--json");
        commands.Last.Input.Should().BeNull();
        commands.Last.Arguments.Should().NotContain("-p");
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

    [Fact]
    public async Task It_runs_in_the_scratch_directory()
    {
        var (refresher, commands) = Create();

        await refresher.TryRefreshAsync();

        commands.Last.WorkingDir.Should().Be(new StubAgentConfigFiles().ScratchDirectory());
    }

    [Fact]
    public async Task It_carries_a_deadline_of_its_own()
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
    public async Task A_run_that_succeeded_reports_the_nudge_as_delivered()
    {
        var (refresher, commands) = Create();

        commands.Result = new CommandResult(0, "{\"loggedIn\":true}", string.Empty);

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
