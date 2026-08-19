using System.Threading.Channels;
using Act.Agents.ClaudeCode;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
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

    // The interrupt is the one turn end no hook reports — measured against 2.1.235, a Ctrl+C produces
    // no payload at all — so this adapter is what tells its sessions to watch for the keystroke. A
    // session launched without it leaves an interrupted card claiming `running` for good.
    [Fact]
    public async Task A_launched_session_reports_the_ctrl_c_interrupt()
    {
        await using var session = await LaunchAsync(new StubPtyHost());

        await session.Terminal.WriteAsync(TurnInterruptKeys.CtrlC);

        (await FirstEventAsync(session)).Should().BeOfType<TurnInterrupted>();
    }

    // A resumed session is the same session, and a retried card is exactly the one a user is likely
    // to interrupt again.
    [Fact]
    public async Task A_resumed_session_reports_the_ctrl_c_interrupt()
    {
        var adapter = Adapter(new StubPtyHost());

        await using var session = await adapter.ResumeAsync(new AgentResumeRequest(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            "pick it back up",
            new LaunchConfig(),
            TerminalSize.Default));

        await session.Terminal.WriteAsync(TurnInterruptKeys.CtrlC);

        (await FirstEventAsync(session)).Should().BeOfType<TurnInterrupted>();
    }

    // `Esc` stops a turn on this CLI just as completely as Ctrl+C and reports just as little, so it is
    // named too. It is the looser of the two — it also dismisses the CLI's own popups — and that was
    // weighed rather than overlooked: an `Esc` that stops the agent is the common case, a card left
    // claiming `running` is the expensive failure, and a badge set a beat early is corrected by the
    // next activity event. See `docs/findings/agent-interrupt.md`.
    [Fact]
    public async Task A_launched_session_reports_the_escape_interrupt()
    {
        await using var session = await LaunchAsync(new StubPtyHost());

        await session.Terminal.WriteAsync(TurnInterruptKeys.Escape);

        (await FirstEventAsync(session)).Should().BeOfType<TurnInterrupted>();
    }

    // The trap that comes with naming `Esc`: an arrow key is the same byte plus more. Nothing about
    // moving the cursor ends a turn, and a card sent up for review by an arrow key would be worse than
    // the bug this whole path fixes.
    [Fact]
    public async Task An_arrow_key_is_not_the_escape_interrupt()
    {
        await using var session = await LaunchAsync(new StubPtyHost());

        await session.Terminal.WriteAsync(TurnInterruptKeys.Escape + "[A");

        (await FirstEventAsync(session, TimeSpan.FromMilliseconds(500))).Should().BeNull();
    }

    // The bug the `Esc` support first shipped with, found live: pressing `Esc` to dismiss the CLI's
    // slash-command list put a card that was still working up for review. Measured against 2.1.235 —
    // an open picker eats the key and the turn carries on, for Ctrl+C exactly as for `Esc` — so the
    // press after a `/` is not reported, and the one after that, which is what really stops the turn,
    // is. The adapter names the triggers; `TurnInterruptWatchTests` covers the rest of the table.
    [Theory]
    [InlineData(TurnInterruptKeys.Escape)]
    [InlineData(TurnInterruptKeys.CtrlC)]
    public async Task A_key_that_only_closes_the_slash_menu_is_not_an_interrupt(string key)
    {
        await using var session = await LaunchAsync(new StubPtyHost());

        await session.Terminal.WriteAsync("/");
        await session.Terminal.WriteAsync(key);

        (await FirstEventAsync(session, TimeSpan.FromMilliseconds(500)))
            .Should().BeNull("the picker consumed it, so nothing about the turn changed");

        await session.Terminal.WriteAsync(key);

        (await FirstEventAsync(session)).Should().BeOfType<TurnInterrupted>();
    }

    // Ordinary typing does not stop the key reaching the turn — measured, `hello there` in the composer
    // and the first `Esc` interrupted — so a card must still move here.
    [Fact]
    public async Task Typing_before_the_key_does_not_suppress_a_real_interrupt()
    {
        await using var session = await LaunchAsync(new StubPtyHost());

        foreach (var character in "hello there")
            await session.Terminal.WriteAsync(character.ToString());

        await session.Terminal.WriteAsync(TurnInterruptKeys.Escape);

        (await FirstEventAsync(session)).Should().BeOfType<TurnInterrupted>();
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
    //
    // The `=` is load-bearing and is a measured trap rather than a style choice — see
    // `Only_the_bound_form_of_add_dir_is_ever_emitted` below.
    [Fact]
    public async Task Every_attachment_is_listed_in_the_prompt_and_its_folder_is_granted()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, attachments: Attached(
            new AgentAttachment(@"C:\act\a\shot.png", IsImage: true),
            new AgentAttachment(@"C:\act\a\trace.log", IsImage: false)));

        pty.Last.Arguments.Should().Contain(@"--add-dir=C:\act\a");
        pty.Last.Arguments[^1].Should().Contain(@"C:\act\a\shot.png").And.Contain(@"C:\act\a\trace.log");
    }

    // The regression guard for the bug attachments shipped with: `--add-dir` is variadic
    // (`--add-dir <directories...>`), so passing the path as its own argument makes the CLI keep
    // eating positionals — and the very next one is the prompt. Measured against 2.1.222, the
    // separated form answers `Error: Input must be provided either through stdin or as a prompt
    // argument`, which in a TUI launch is silent: the agent simply comes up with an empty composer.
    //
    // A test cannot re-derive commander's parsing, so what it pins is the *form* — the bound one is
    // emitted and the bare one never is, on every door the adapter has.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_the_bound_form_of_add_dir_is_ever_emitted(bool resuming)
    {
        var pty = new StubPtyHost();
        var adapter = new ClaudeCodeAdapter(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<ClaudeCodeAdapter>.Instance);
        var attachments = Attached(new AgentAttachment(@"C:\act\a\trace.log", IsImage: false));

        await using var session = resuming
            ? await adapter.ResumeAsync(new AgentResumeRequest(
                TaskId, SessionId, "C:/repo", "do the thing", "pick it back up",
                new LaunchConfig(), TerminalSize.Default, attachments))
            : await LaunchAsync(pty, attachments: attachments);

        pty.Last.Arguments.Should().NotContain("--add-dir");
        pty.Last.Arguments.Should().Contain(argument =>
            argument.StartsWith("--add-dir=", StringComparison.Ordinal));

        // The other half of the same rule: whatever the flags, the prompt is still the final argument.
        // A launch has the file list appended to it; a resume is the bare message.
        if (resuming)
            pty.Last.Arguments[^1].Should().Be("pick it back up");
        else
            pty.Last.Arguments[^1].Should().StartWith("do the thing");
    }

    // Granted whether or not there is a file yet, because a file dropped onto the terminal an hour in
    // has no second chance at this command line.
    [Fact]
    public async Task The_folder_is_granted_even_with_nothing_attached_yet()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, attachments: Attached());

        pty.Last.Arguments.Should().Contain(@"--add-dir=C:\act\a");
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

        pty.Last.Arguments.Should().Contain(@"--add-dir=C:\act\a");
        pty.Last.Arguments[^1].Should().Be("pick it back up");
    }

    // No directory means no grant, which is what a card launched before attachments existed looks
    // like — an empty `--add-dir` would be a flag pointing at the process's own working directory.
    [Fact]
    public async Task Nothing_is_granted_when_there_is_no_attachment_directory()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty);

        pty.Last.Arguments.Should().NotContain(argument =>
            argument.StartsWith("--add-dir", StringComparison.Ordinal));
    }

    // The task form lists the models in the order the catalog declares them, so the catalog's
    // order is the dropdown's order — strongest first, weakest last.
    [Fact]
    public void The_models_are_listed_strongest_to_weakest()
        => ClaudeCodeCapabilities.Current.Models.Select(model => model.Slug)
            .Should().Equal("fable", "opus", "sonnet", "haiku");

    private static AgentAttachments Attached(params AgentAttachment[] files)
        => new(@"C:\act\a", files);

    // Bounded on both paths: null means "nothing arrived in time", which is an assertion of its own
    // for the keys this adapter deliberately does not watch.
    private static async Task<AgentEvent?> FirstEventAsync(IAgentSession session, TimeSpan? within = null)
    {
        using var giveUp = new CancellationTokenSource(within ?? TimeSpan.FromSeconds(5));

        try
        {
            await foreach (var observed in session.Events.WithCancellation(giveUp.Token))
                return observed;
        }
        catch (Exception error) when (error is OperationCanceledException or ChannelClosedException)
        {
        }

        return null;
    }

    private static ClaudeCodeAdapter Adapter(StubPtyHost pty)
        => new(pty, new StubCommandHost(), new TestClock(), new StubHookEndpoint(), new StubAgentConfigFiles(), NullLogger<ClaudeCodeAdapter>.Instance);

    private static Task<IAgentSession> LaunchAsync(
        StubPtyHost pty,
        LaunchConfig? config = null,
        AgentAttachments? attachments = null)
        => Adapter(pty).LaunchAsync(new AgentLaunchRequest(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            config ?? new LaunchConfig(),
            TerminalSize.Default,
            attachments));
}
