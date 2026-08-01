using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// Step 6's verify line: a scripted session driven end to end through the seam. Columns and
// badges do not move yet — the rules engine that reads these events arrives at step 9.
public class MockAgentSessionTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    private const string SessionId = "6f0d5d5c-0000-4a2c-9f4d-2f0a3f7c1e11";

    [Fact]
    public async Task A_turn_blocked_on_a_permission_prompt_reports_every_step_in_order()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start()
                .Activity("Read")
                .RequestsPermission("push the current branch")
                .Paints("Do you want to proceed?\r\n\u276f 1. Yes\r\n")
                .AwaitsKeystroke()
                .Activity("Bash")
                .EndsTurn()
                .SessionEnds(),
        };

        await using var session = await LaunchAsync(adapter);

        var seen = new List<AgentEvent>();

        await foreach (var received in session.Events)
        {
            seen.Add(received);

            // The user answers in the terminal, which is the only channel there is. ACT
            // observes the prompt; it never responds to one.
            if (received is PermissionRequested)
                await session.Terminal.WriteAsync("1\r");
        }

        seen.Select(received => received.GetType()).Should().Equal(
        [
            typeof(SessionStarted),
            typeof(ActivityObserved),
            typeof(PermissionRequested),
            typeof(ActivityObserved),
            typeof(TurnEnded),
            typeof(SessionEnded),
        ]);

        seen.Should().AllSatisfy(received => received.SessionId.Should().Be(SessionId));
        seen.Select(received => received.At).Should().BeInAscendingOrder();

        session.Received.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new AgentInput(AgentInputKind.Write, "1\r"));
    }

    [Fact]
    public async Task Terminal_output_accumulates_into_a_backlog_a_reattaching_view_can_replay()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start()
                .Paints("Claude Code v2\r\n")
                .Paints("\u276f ")
                .EndsTurn(),
        };

        await using var session = await LaunchAsync(adapter);

        await Drain(session);

        session.Terminal.Backlog.Should().Be("Claude Code v2\r\n\u276f ");
    }

    [Fact]
    public async Task Live_terminal_output_reaches_an_attached_view()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start()
                .AwaitsKeystroke()
                .Paints("working\u2026")
                .EndsTurn(),
        };

        await using var session = await LaunchAsync(adapter);

        var painted = new List<string>();

        session.Terminal.Output += chunk =>
        {
            painted.Add(chunk);

            return Task.CompletedTask;
        };

        await session.Terminal.WriteAsync("do the thing\r");
        await Drain(session);

        painted.Should().Equal("working\u2026");
    }

    // The whole input surface, and the point of asserting it exhaustively: everything that reaches
    // a session is either the user's own keystrokes on their way through or ACT resizing the pty.
    // ACT composes nothing \u2014 no prompt answer, no send-back, no approval.
    [Fact]
    public async Task Nothing_but_the_users_keystrokes_reaches_the_session()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start().AwaitsKeystroke().EndsTurn(),
        };

        await using var session = await LaunchAsync(adapter);

        session.Terminal.Resize(100, 40);
        await session.Terminal.WriteAsync("do the thing\r");
        await Drain(session);

        session.Received.Should().BeEquivalentTo(
        [
            new AgentInput(AgentInputKind.Resize, Size: new TerminalSize(100, 40)),
            new AgentInput(AgentInputKind.Write, "do the thing\r"),
        ]);
    }

    [Fact]
    public async Task Killing_a_live_session_ends_the_stream_with_a_kill()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start().Activity().AwaitsKeystroke().EndsTurn(),
        };

        await using var session = await LaunchAsync(adapter);

        var seen = new List<AgentEvent>();

        await foreach (var received in session.Events)
        {
            seen.Add(received);

            if (received is ActivityObserved)
                await session.KillAsync();
        }

        seen.Last().Should().BeOfType<SessionKilled>();
        seen.Should().NotContain(received => received is TurnEnded);
    }

    [Fact]
    public async Task Disposing_a_live_session_completes_the_stream()
    {
        var adapter = new MockAgentAdapter
        {
            Script = AgentScript.Start().AwaitsKeystroke().EndsTurn(),
        };

        var session = await LaunchAsync(adapter);

        var seen = new List<AgentEvent>();

        await foreach (var received in session.Events)
        {
            seen.Add(received);
            await session.DisposeAsync();
        }

        seen.Should().ContainSingle().Which.Should().BeOfType<SessionStarted>();
    }

    [Fact]
    public async Task A_launch_carries_the_pre_minted_session_id_the_task_text_and_a_terminal_size()
    {
        var adapter = new MockAgentAdapter();

        await using var session = await LaunchAsync(adapter);

        session.SessionId.Should().Be(SessionId);

        var launch = adapter.Launches.Should().ContainSingle().Subject;

        launch.InitialPrompt.Should().Be("do the thing");
        launch.Size.Should().Be(TerminalSize.Default);
    }

    [Fact]
    public async Task Resuming_reuses_the_same_session_id()
    {
        var adapter = new MockAgentAdapter();

        await using var session = await adapter.ResumeAsync(new AgentResumeRequest(
            TaskId,
            SessionId,
            "C:/repo",
            "do the thing",
            "actually target main",
            new LaunchConfig(),
            TerminalSize.Default));

        session.SessionId.Should().Be(SessionId);
        adapter.Resumes.Should().ContainSingle().Which.Message.Should().Be("actually target main");
    }

    [Fact]
    public async Task A_config_the_adapter_cannot_honour_never_launches()
    {
        var adapter = new MockAgentAdapter();
        var request = Launch(new LaunchConfig { Model = "no-such-model" });

        var launch = async () => await adapter.LaunchAsync(request);

        await launch.Should().ThrowAsync<InvalidOperationException>()
            .Where(error => error.Message.Contains("no-such-model"));
        adapter.Launches.Should().BeEmpty();
    }

    private static async Task<MockAgentSession> LaunchAsync(MockAgentAdapter adapter)
        => (MockAgentSession)await adapter.LaunchAsync(Launch());

    private static AgentLaunchRequest Launch(LaunchConfig? config = null) => new(
        TaskId,
        SessionId,
        "C:/repo",
        "do the thing",
        config ?? new LaunchConfig(),
        TerminalSize.Default);

    private static async Task Drain(IAgentSession session)
    {
        await foreach (var _ in session.Events)
        {
        }
    }

    private static async Task<TEvent> LastAsync<TEvent>(IAgentSession session)
        where TEvent : AgentEvent
    {
        TEvent? last = null;

        await foreach (var received in session.Events)
        {
            if (received is TEvent match)
                last = match;
        }

        last.Should().NotBeNull();

        return last;
    }
}
