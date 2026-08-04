using Act.Agents.ClaudeCode;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

public class ClaudeCodeAdapterContractTests : AgentAdapterContract
{
    protected override IAgentAdapter CreateAdapter()
        => new ClaudeCodeAdapter(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles());
}

public class ClaudeCodeAdapterTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    private const string SessionId = "39694aa9-918e-471b-8623-61d0d948ffbb";

    [Fact]
    public async Task A_launch_carries_the_pre_minted_session_id()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty);

        pty.Last.Arguments.Should().ContainInOrder("--session-id", SessionId);
        session.SessionId.Should().Be(SessionId);
    }

    // The regression guard for the launch bug found verifying step 7: the opening prompt used to be
    // typed into the TUI once it had painted and gone quiet, and a CLI that has painted its banner
    // is not yet listening — the prompt went nowhere and the card sat at an empty composer.
    [Fact]
    public async Task The_opening_prompt_is_positional_and_is_the_task_text_alone()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty);

        pty.Last.Arguments[^1].Should().Be("do the thing");
    }

    [Fact]
    public async Task Nothing_is_typed_into_the_terminal_at_launch()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty);

        pty.LastProcess.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Resuming_passes_the_session_id_and_the_message_positionally()
    {
        var pty = new StubPtyHost();
        var adapter = new ClaudeCodeAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles());

        await using var session = await adapter.ResumeAsync(new AgentResumeRequest(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            "pick it back up",
            new LaunchConfig(),
            TerminalSize.Default));

        pty.Last.Arguments.Should().ContainInOrder("--resume", SessionId);
        pty.Last.Arguments[^1].Should().Be("pick it back up");
        pty.LastProcess.Writes.Should().BeEmpty();
    }

    // A bare resume drops the user at the prompt, so there must be no positional argument at all —
    // an empty one would be read as a prompt and start a turn nobody asked for.
    [Fact]
    public async Task A_bare_resume_adds_no_prompt_argument()
    {
        var pty = new StubPtyHost();
        var adapter = new ClaudeCodeAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles());

        await using var session = await adapter.ResumeAsync(new AgentResumeRequest(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            null,
            new LaunchConfig(),
            TerminalSize.Default));

        pty.Last.Arguments.Should().NotContain("do the thing");
        pty.Last.Arguments[^1].Should().NotBe(string.Empty);
    }

    private static Task<IAgentSession> LaunchAsync(StubPtyHost pty, LaunchConfig? config = null)
        => new ClaudeCodeAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles()).LaunchAsync(new AgentLaunchRequest(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            config ?? new LaunchConfig(),
            TerminalSize.Default));
}
