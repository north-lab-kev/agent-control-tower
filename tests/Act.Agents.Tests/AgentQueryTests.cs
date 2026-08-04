using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// The one-shot query, held to the properties that make it cheap and safe rather than to a verbatim
// command line — except where a flag *is* the property, in which case it is named. The measured
// behaviour behind each of these is in `docs/agent-title-findings.md`.
public class AgentQueryTests
{
    public static TheoryData<string> Agents => ["claude", "codex"];

    private static (IAgentAdapter Adapter, StubCommandHost Commands) Create(
        string agent,
        AgentDefaults? machine = null)
    {
        var commands = new StubCommandHost();
        var files = new StubAgentConfigFiles();

        IAgentAdapter adapter = agent == "codex"
            ? new CodexAdapter(new StubPtyHost(), commands, new TestClock(), new StubHookEndpoint(), files)
            : new ClaudeCodeAdapter(new StubPtyHost(), commands, new TestClock(), new StubHookEndpoint(), files);

        return (adapter, commands);
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task It_runs_the_cheapest_model_the_agent_offers(string agent)
    {
        var (adapter, commands) = Create(agent);

        await adapter.QueryAsync(new AgentQueryRequest("name this"));

        var utility = adapter.Capabilities.Utility!;

        utility.Slug.Should().NotBe(
            adapter.Capabilities.DefaultModel,
            "a query that runs on the model the work runs on is not a cheap query");

        commands.Last.Arguments.Should().Contain(utility.Slug);
    }

    // Lowest on the model's own ladder, not a literal "low": Codex gives each model a different one,
    // and the bottom rung is the only rung that means the same thing on all of them.
    [Theory]
    [MemberData(nameof(Agents))]
    public async Task It_runs_at_the_bottom_of_that_models_effort_ladder(string agent)
    {
        var (adapter, commands) = Create(agent);

        await adapter.QueryAsync(new AgentQueryRequest("name this"));

        var cheapest = adapter.Capabilities.Utility!.Efforts[0];

        commands.Last.Arguments.Should().Contain(
            argument => argument.Contains(cheapest, StringComparison.Ordinal));
    }

    // The whole reason `CommandStartInfo` has an `Input`: the prompt is the only argument made of
    // text ACT did not write, and it never touches a command line.
    [Theory]
    [MemberData(nameof(Agents))]
    public async Task The_prompt_travels_on_standard_input_and_never_as_an_argument(string agent)
    {
        var (adapter, commands) = Create(agent);

        await adapter.QueryAsync(new AgentQueryRequest("a prompt with \"quotes\" and spaces"));

        commands.Last.Input.Should().Be("a prompt with \"quotes\" and spaces");
        commands.Last.Arguments.Should().NotContain("a prompt with \"quotes\" and spaces");
    }

    // Not the task's directory and not the user's: both CLIs read what they start in, and a question
    // about a sentence has no business loading a repository.
    [Theory]
    [MemberData(nameof(Agents))]
    public async Task It_runs_in_the_scratch_directory(string agent)
    {
        var (adapter, commands) = Create(agent);

        await adapter.QueryAsync(new AgentQueryRequest("name this"));

        commands.Last.WorkingDir.Should().Be(new StubAgentConfigFiles().ScratchDirectory());
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task It_carries_no_hook_wiring(string agent)
    {
        var (adapter, commands) = Create(agent);

        await adapter.QueryAsync(new AgentQueryRequest("name this"));

        commands.Last.Environment.Should().NotContainKeys(
            "ACT_TASK_ID",
            "ACT_HOOK_TOKEN",
            "ACT_HOOK_ENDPOINT");
    }

    // The install applies; the user's launch preferences do not. An `ExtraFlags` carrying `--model`
    // or a permission mode would quietly turn a throwaway into whatever the user last set.
    [Theory]
    [MemberData(nameof(Agents))]
    public async Task It_honours_the_machines_binary_and_environment_but_not_its_extra_flags(string agent)
    {
        var (adapter, commands) = Create(agent);

        var machine = new AgentDefaults
        {
            Binary = @"C:\tools\my-cli.exe",
            ExtraFlags = ["--model", "something-expensive"],
            Env = { ["MY_VAR"] = "set" },
        };

        await adapter.QueryAsync(new AgentQueryRequest("name this", machine));

        commands.Last.Executable.Should().Be(@"C:\tools\my-cli.exe");
        commands.Last.Environment.Should().Contain(new KeyValuePair<string, string>("MY_VAR", "set"));
        commands.Last.Arguments.Should().NotContain("something-expensive");
    }

    // Someone is waiting on this one, so the query's own deadline reaches the host rather than
    // leaving it on the 90-second ceiling a background run would be fine with.
    [Theory]
    [MemberData(nameof(Agents))]
    public async Task The_querys_deadline_reaches_the_host(string agent)
    {
        var (adapter, commands) = Create(agent);

        await adapter.QueryAsync(new AgentQueryRequest("name this") { Timeout = TimeSpan.FromSeconds(7) });

        commands.Last.Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task An_answer_comes_back_verbatim(string agent)
    {
        var (adapter, commands) = Create(agent);

        commands.Result = new CommandResult(0, "Consolidate the badge colours", string.Empty);

        (await adapter.QueryAsync(new AgentQueryRequest("name this")))
            .Should().Be("Consolidate the badge colours");
    }

    // Whatever it wrote on the way out is not an answer. A caller that treated a stderr dump as a
    // title would put a stack trace on the board.
    [Theory]
    [MemberData(nameof(Agents))]
    public async Task A_failed_run_answers_nothing(string agent)
    {
        var (adapter, commands) = Create(agent);

        commands.Result = new CommandResult(1, "Consolidate the badge colours", "not logged in");

        (await adapter.QueryAsync(new AgentQueryRequest("name this"))).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Agents))]
    public async Task A_run_that_timed_out_answers_nothing(string agent)
    {
        var (adapter, commands) = Create(agent);

        commands.Result = new CommandResult(0, "half an ans", string.Empty, TimedOut: true);

        (await adapter.QueryAsync(new AgentQueryRequest("name this"))).Should().BeNull();
    }

    // Print mode, and named explicitly: it is the single flag that makes the run non-interactive, and
    // without it the adapter would spawn a TUI with no terminal attached.
    [Fact]
    public async Task Claude_Code_queries_in_print_mode_with_its_customisations_off()
    {
        var (adapter, commands) = Create("claude");

        await adapter.QueryAsync(new AgentQueryRequest("name this"));

        commands.Last.Arguments.Should().Contain("-p");

        // `--safe-mode`, not `--bare`: the latter drops OAuth and the keychain, so a subscription
        // user's query would fail to authenticate at all.
        commands.Last.Arguments.Should().Contain("--safe-mode");
        commands.Last.Arguments.Should().NotContain("--bare");
        commands.Last.Arguments.Should().Contain("--no-session-persistence");
    }

    // Worth ~25,000 tokens a title: the tool schemas are most of what the CLI sends and this question
    // touches nothing. It must stay **last** — `--tools` is variadic, so anything appended after it is
    // read as another tool name rather than as a flag of its own.
    [Fact]
    public async Task Claude_Code_queries_with_every_tool_disabled_as_the_last_argument()
    {
        var (adapter, commands) = Create("claude");

        await adapter.QueryAsync(new AgentQueryRequest("name this"));

        commands.Last.Arguments[^2].Should().Be("--tools");
        commands.Last.Arguments[^1].Should().BeEmpty();
    }

    // `exec` for non-interactive, `--ignore-user-config` for no profile/hooks/MCP with auth intact,
    // and `-` because Codex would otherwise append redirected stdin to a positional prompt.
    [Fact]
    public async Task Codex_queries_through_exec_with_the_users_config_ignored()
    {
        var (adapter, commands) = Create("codex");

        await adapter.QueryAsync(new AgentQueryRequest("name this"));

        commands.Last.Arguments[0].Should().Be("exec");
        commands.Last.Arguments.Should().Contain("--ignore-user-config");
        commands.Last.Arguments.Should().Contain("--ephemeral");
        commands.Last.Arguments.Should().ContainInOrder("--sandbox", "read-only");
        commands.Last.Arguments.Should().Contain("--skip-git-repo-check");
        commands.Last.Arguments[^1].Should().Be("-");
        commands.Last.Arguments.Should().NotContain("--profile");
    }
}
