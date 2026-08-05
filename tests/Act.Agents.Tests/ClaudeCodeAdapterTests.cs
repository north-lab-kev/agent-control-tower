using Act.Agents.ClaudeCode;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.Agents.Tests;

public class ClaudeCodeAdapterContractTests : AgentAdapterContract
{
    protected override IAgentAdapter CreateAdapter()
        => new ClaudeCodeAdapter(new StubPtyHost(), new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<ClaudeCodeAdapter>.Instance);
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
        var adapter = new ClaudeCodeAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<ClaudeCodeAdapter>.Instance);

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
        var adapter = new ClaudeCodeAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<ClaudeCodeAdapter>.Instance);

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

    // There is no `--image` on this CLI, so every attachment reaches the model as a path — and the
    // paths are all outside the working directory, which is what `--add-dir` is for: without it a
    // `default`-mode task would park on a permission prompt for its own attachment.
    [Fact]
    public async Task Every_attachment_is_listed_in_the_prompt_and_its_folder_is_granted()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, attachments: Attached(
            new AgentAttachment(@"C:\act\a\shot.png", IsImage: true),
            new AgentAttachment(@"C:\act\a\trace.log", IsImage: false)));

        pty.Last.Arguments.Should().ContainInOrder("--add-dir", @"C:\act\a");
        pty.Last.Arguments[^1].Should().Contain(@"C:\act\a\shot.png").And.Contain(@"C:\act\a\trace.log");
    }

    // Granted whether or not there is a file yet, because a file dropped onto the terminal an hour in
    // has no second chance at this command line.
    [Fact]
    public async Task The_folder_is_granted_even_with_nothing_attached_yet()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, attachments: Attached());

        pty.Last.Arguments.Should().ContainInOrder("--add-dir", @"C:\act\a");
        pty.Last.Arguments[^1].Should().Be("do the thing");
    }

    // A resume keeps the grant and drops the list: the transcript it just reopened already carries
    // the files, and re-listing them would read as a fresh handover forty turns in.
    [Fact]
    public async Task A_resume_keeps_the_grant_and_does_not_relist_the_files()
    {
        var pty = new StubPtyHost();
        var adapter = new ClaudeCodeAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<ClaudeCodeAdapter>.Instance);

        await using var session = await adapter.ResumeAsync(new AgentResumeRequest(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            "pick it back up",
            new LaunchConfig(),
            TerminalSize.Default,
            Attached(new AgentAttachment(@"C:\act\a\trace.log", IsImage: false))));

        pty.Last.Arguments.Should().ContainInOrder("--add-dir", @"C:\act\a");
        pty.Last.Arguments[^1].Should().Be("pick it back up");
    }

    // No directory means no grant, which is what a card launched before attachments existed looks
    // like — an empty `--add-dir` would be a flag pointing at the process's own working directory.
    [Fact]
    public async Task Nothing_is_granted_when_there_is_no_attachment_directory()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty);

        pty.Last.Arguments.Should().NotContain("--add-dir");
    }

    private static AgentAttachments Attached(params AgentAttachment[] files)
        => new(@"C:\act\a", files);

    private static Task<IAgentSession> LaunchAsync(
        StubPtyHost pty,
        LaunchConfig? config = null,
        AgentAttachments? attachments = null)
        => new ClaudeCodeAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<ClaudeCodeAdapter>.Instance).LaunchAsync(new AgentLaunchRequest(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            config ?? new LaunchConfig(),
            TerminalSize.Default,
            attachments));
}
