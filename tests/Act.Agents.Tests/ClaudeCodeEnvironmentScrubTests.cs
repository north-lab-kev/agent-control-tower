using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.Agents.Tests;

public class ClaudeCodeEnvironmentScrubTests
{
    private const string Marker = "CLAUDECODE";

    private const string Entrypoint = "CLAUDE_CODE_ENTRYPOINT";

    [Theory]
    [InlineData(Marker)]
    [InlineData(Entrypoint)]
    [InlineData("CLAUDE_CODE_SESSION_ID")]
    [InlineData("CLAUDE_CODE_HOST_SESSION_ID")]
    [InlineData("CLAUDE_CODE_CHILD_SESSION")]
    [InlineData("CLAUDE_CODE_SDK_HAS_OAUTH_REFRESH")]
    [InlineData("CLAUDE_AGENT_SDK_VERSION")]
    [InlineData("CLAUDE_PID")]
    public void The_enclosing_sessions_own_variables_do_not_reach_the_agent(string key)
        => Scrubbed(Session()).Should().NotContainKey(key);

    // The prefix rather than a list of the variables that were measured: the set differs by host
    // already — a desktop session carries variables a terminal one does not — so a named list would
    // be one CLI release from letting a new marker through in silence.
    [Fact]
    public void A_variable_a_later_cli_invents_goes_with_them()
        => Scrubbed(Session()).Should().NotContainKey("CLAUDE_CODE_SOMETHING_NEW");

    [Fact]
    public void Nothing_else_is_touched()
    {
        var scrubbed = Scrubbed(Session());

        scrubbed["PATH"].Should().Be(@"C:\tools");
        scrubbed["ANTHROPIC_BASE_URL"].Should().Be("https://api.anthropic.com");
        scrubbed["HTTPS_PROXY"].Should().Be("http://proxy:8080");
    }

    [Fact]
    public void The_config_directory_survives_because_ACT_reads_it_too()
        => Scrubbed(Session())["CLAUDE_CONFIG_DIR"].Should().Be(@"C:\claude");

    // Without the marker ACT was not launched from inside a session, so a `CLAUDE_*` here is the
    // user's own machine config and removing it would silently undo their setting.
    [Fact]
    public void Outside_a_session_the_users_own_claude_variables_are_left_alone()
    {
        var environment = Session();
        environment.Remove(Marker);

        var scrubbed = Scrubbed(environment);

        scrubbed.Should().ContainKey("CLAUDE_CODE_MAX_CONTEXT_TOKENS");
        scrubbed.Should().ContainKey(Entrypoint);
    }

    // The cost of the prefix, stated rather than discovered: inside a session a knob the user set
    // machine-wide goes too, because nothing distinguishes it from what the session injected. The
    // install's own `Env` is where it comes back, and that is the setting's documented purpose.
    [Fact]
    public void Inside_a_session_a_machine_wide_knob_goes_with_the_markers()
        => Scrubbed(Session()).Should().NotContainKey("CLAUDE_CODE_MAX_CONTEXT_TOKENS");

    // A `Dictionary` built without a comparer is what a caller other than `AgentEnvironment` would
    // hand over, and on Linux its keys really are case-sensitive.
    [Fact]
    public void The_marker_is_recognised_however_it_was_spelled()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["claudecode"] = "1",
            ["claude_code_entrypoint"] = "cli",
            ["PATH"] = @"C:\tools",
        };

        var scrubbed = Scrubbed(environment);

        scrubbed.Should().ContainSingle().Which.Key.Should().Be("PATH");
    }

    [Fact]
    public void With_no_enclosing_session_the_environment_is_left_as_it_was()
    {
        var environment = new Dictionary<string, string> { ["PATH"] = @"C:\tools" };

        var scrubbed = Scrubbed(environment);

        scrubbed.Should().ContainSingle().Which.Key.Should().Be("PATH");
    }

    private static Dictionary<string, string> Scrubbed(Dictionary<string, string> environment)
    {
        ClaudeCodeEnvironmentScrub.Shared.Apply(environment);

        return environment;
    }

    private static Dictionary<string, string> Session() => new(StringComparer.OrdinalIgnoreCase)
    {
        [Marker] = "1",
        [Entrypoint] = "claude-desktop",
        ["CLAUDE_CODE_SESSION_ID"] = "9f0975c2",
        ["CLAUDE_CODE_HOST_SESSION_ID"] = "local_50b22dc5",
        ["CLAUDE_CODE_CHILD_SESSION"] = "1",
        ["CLAUDE_CODE_SDK_HAS_OAUTH_REFRESH"] = "1",
        ["CLAUDE_CODE_SOMETHING_NEW"] = "1",
        ["CLAUDE_AGENT_SDK_VERSION"] = "0.3.222",
        ["CLAUDE_PID"] = "29716",
        ["CLAUDE_CODE_MAX_CONTEXT_TOKENS"] = "1000000",
        ["CLAUDE_CONFIG_DIR"] = @"C:\claude",
        ["ANTHROPIC_BASE_URL"] = "https://api.anthropic.com",
        ["HTTPS_PROXY"] = "http://proxy:8080",
        ["PATH"] = @"C:\tools",
    };
}

// The scrub reaching a real launch, which is the part the unit tests above cannot see: the adapter
// has to ask for it, and only one of the two adapters does.
[Collection(EnvironmentCollection.Name)]
public class NestedSessionLaunchTests
{
    private const string Marker = "CLAUDECODE";

    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    [Fact]
    public Task Claude_Code_spawned_from_inside_a_session_does_not_inherit_it()
        => InsideASession(async () =>
        {
            var pty = new StubPtyHost();

            await using var session = await new ClaudeCodeAdapter(
                pty,
                new StubCommandHost(),
                new TestClock(),
                new StubHookEndpoint(),
                new StubAgentConfigFiles(),
                NullLogger<ClaudeCodeAdapter>.Instance).LaunchAsync(Request("39694aa9-918e-471b-8623-61d0d948ffbb"));

            pty.Last.Environment.Should().NotContainKey(Marker);
            pty.Last.Environment.Should().NotContainKey("CLAUDE_CODE_ENTRYPOINT");
        });

    // A title query runs the same CLI, so it inherits the same session it must not join.
    [Fact]
    public Task A_Claude_Code_query_from_inside_a_session_does_not_inherit_it_either()
        => InsideASession(async () =>
        {
            var commands = new StubCommandHost();

            await new ClaudeCodeAdapter(
                new StubPtyHost(),
                commands,
                new TestClock(),
                new StubHookEndpoint(),
                new StubAgentConfigFiles(),
                NullLogger<ClaudeCodeAdapter>.Instance).QueryAsync(new AgentQueryRequest("name this"));

            commands.Last.Environment.Should().NotContainKey(Marker);
            commands.Last.Environment.Should().NotContainKey("CLAUDE_CODE_ENTRYPOINT");
        });

    // Deliberate, not an oversight: nothing was measured about what a Codex session passes down, so
    // Codex subtracts nothing and this test is what says so out loud.
    [Fact]
    public Task Codex_subtracts_nothing_because_nothing_was_measured()
        => InsideASession(async () =>
        {
            var pty = new StubPtyHost();

            await using var session = await new CodexAdapter(
                pty,
                new StubCommandHost(),
                new TestClock(),
                new StubHookEndpoint(),
                new StubAgentConfigFiles(),
                NullLogger<CodexAdapter>.Instance).LaunchAsync(Request("ignored-by-codex"));

            pty.Last.Environment.Should().ContainKey(Marker);
        });

    [Fact]
    public Task A_Codex_query_subtracts_nothing_either()
        => InsideASession(async () =>
        {
            var commands = new StubCommandHost();

            await new CodexAdapter(
                new StubPtyHost(),
                commands,
                new TestClock(),
                new StubHookEndpoint(),
                new StubAgentConfigFiles(),
                NullLogger<CodexAdapter>.Instance).QueryAsync(new AgentQueryRequest("name this"));

            commands.Last.Environment.Should().ContainKey(Marker);
        });

    private static AgentLaunchRequest Request(string sessionId) => new(
        TaskId,
        sessionId,
        "C:/repo",
        "do the thing",
        new LaunchConfig(),
        TerminalSize.Default);

    private static async Task InsideASession(Func<Task> body)
    {
        var marker = Environment.GetEnvironmentVariable(Marker);
        var entrypoint = Environment.GetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT");

        try
        {
            Environment.SetEnvironmentVariable(Marker, "1");
            Environment.SetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT", "cli");

            await body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Marker, marker);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT", entrypoint);
        }
    }
}
