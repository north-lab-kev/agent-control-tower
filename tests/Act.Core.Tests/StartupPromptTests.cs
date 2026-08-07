using System.Threading.Channels;
using Act.Core.Agents;
using Act.Core.Events;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Core.Tests;

// The pre-session prompt, from the side that reports it. The grace is a constructor knob precisely
// so these run in milliseconds instead of the eight seconds a real launch is given.
public class StartupPromptTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_launch_that_produces_no_hook_reports_the_startup_prompt()
    {
        await using var session = Session(new StubPtyProcess());

        (await NextAsync(session)).Should().BeOfType<StartupPromptWaiting>();
    }

    // Terminal output is *not* the disarm signal, and this is the case that says why: a CLI sitting
    // on its trust prompt paints a full screen and has still started nothing.
    [Fact]
    public async Task Terminal_output_alone_does_not_disarm_the_watch()
    {
        var process = new StubPtyProcess();

        await using var session = Session(process);

        await process.EmitAsync("Is this a project you created or one you trust?");

        (await NextAsync(session)).Should().BeOfType<StartupPromptWaiting>();
    }

    [Fact]
    public async Task A_hook_arriving_in_time_disarms_the_watch()
    {
        await using var session = Session(new StubPtyProcess());

        session.Publish(new SessionStarted("s", DateTimeOffset.UnixEpoch, "t", "c"));

        (await NextAsync(session)).Should().BeOfType<SessionStarted>();
        (await NextAsync(session, Grace * 10)).Should().BeNull();
    }

    [Fact]
    public async Task A_process_that_exits_first_reports_the_exit_and_nothing_else()
    {
        var process = new StubPtyProcess();

        await using var session = Session(process);

        process.Exit(1);

        (await NextAsync(session)).Should().BeOfType<ProcessExited>();
    }

    private static PtyAgentSession Session(StubPtyProcess process)
        => new(Guid.NewGuid(), "s", process, new TestClock(), Grace);

    // Null means "nothing arrived in time", which is an assertion of its own here — so the wait is
    // bounded on both paths rather than left to hang the run.
    private static async Task<AgentEvent?> NextAsync(PtyAgentSession session, TimeSpan? within = null)
    {
        using var giveUp = new CancellationTokenSource(within ?? Patience);

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
}
