using System.Threading.Channels;
using Act.Core.Agents;
using Act.Core.Events;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Core.Tests;

// The interrupt, from the side that reports it. It is the second thing no hook reports — measured
// against `claude-code 2.1.235`, a Ctrl+C ends the turn and produces no payload of any kind — so the
// keystroke ACT was asked to forward is the only evidence there is, and these pin that ACT reads it
// exactly and nothing else. `RulesEngineTests` covers what the event then does to a card.
//
// The grace is a minute rather than zero so the startup watch cannot write into the stream a test is
// reading; the one test that wants it says so.
public class TurnInterruptTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan LongEnoughToBeSure = TimeSpan.FromMilliseconds(500);

    private readonly StubPtyProcess process = new();

    [Fact]
    public async Task The_interrupt_key_is_reported_as_a_turn_interrupted()
    {
        await using var session = Session(TurnInterruptKeys.CtrlC);

        await session.Terminal.WriteAsync(TurnInterruptKeys.CtrlC);

        var observed = await NextAsync(session);

        observed.Should().BeOfType<TurnInterrupted>();
        observed!.SessionId.Should().Be("s-1");
    }

    // The whole point is that the agent is interrupted: a signal ACT reported but swallowed would
    // leave the card up for review beside a session still working.
    [Fact]
    public async Task The_interrupt_key_still_reaches_the_agent()
    {
        await using var session = Session(TurnInterruptKeys.CtrlC);

        await session.Terminal.WriteAsync(TurnInterruptKeys.CtrlC);

        process.Writes.Should().Equal(TurnInterruptKeys.CtrlC);
    }

    [Fact]
    public async Task Ordinary_typing_reports_nothing()
    {
        await using var session = Session(TurnInterruptKeys.CtrlC);

        await session.Terminal.WriteAsync("carry on\r");

        (await NextAsync(session, LongEnoughToBeSure)).Should().BeNull();
    }

    // Exact match, never a search: a paste, or a dropped file's path, is one write of many characters
    // and one of them being the interrupt byte says nothing about a turn ending.
    [Fact]
    public async Task A_chunk_that_merely_contains_the_byte_reports_nothing()
    {
        await using var session = Session(TurnInterruptKeys.CtrlC);

        await session.Terminal.WriteAsync($"before{TurnInterruptKeys.CtrlC}after");

        (await NextAsync(session, LongEnoughToBeSure)).Should().BeNull();
    }

    [Fact]
    public async Task Every_key_the_adapter_named_is_reported()
    {
        foreach (var key in new[] { TurnInterruptKeys.CtrlC, TurnInterruptKeys.Escape })
        {
            await using var session = Session(TurnInterruptKeys.CtrlC, TurnInterruptKeys.Escape);

            await session.Terminal.WriteAsync(key);

            (await NextAsync(session)).Should().BeOfType<TurnInterrupted>($"0x{(int)key[0]:X2} was named");
        }
    }

    // The session watches only what it was handed: a control key with its own meaning in the TUI —
    // Ctrl+L clears the screen — is nobody's interrupt and must stay a plain keystroke.
    [Fact]
    public async Task A_key_the_adapter_did_not_name_reports_nothing()
    {
        await using var session = Session(TurnInterruptKeys.CtrlC, TurnInterruptKeys.Escape);

        await session.Terminal.WriteAsync("\f");

        (await NextAsync(session, LongEnoughToBeSure)).Should().BeNull();
    }

    // The trap `Escape` brings with it, and the reason the match is on the whole chunk: every arrow
    // key, function key and mouse report *starts* with that same byte, and Alt+Escape sends it twice.
    // A substring match would report an interrupt every time the user pressed an arrow key.
    [Theory]
    [InlineData("[A")]
    [InlineData("[B")]
    [InlineData("[1;5D")]
    [InlineData("OP")]

    // Alt+Escape: the byte twice, which is a chunk of its own and not the key on its own.
    [InlineData(TurnInterruptKeys.Escape)]
    public async Task An_escape_sequence_is_not_the_escape_key(string rest)
    {
        await using var session = Session(TurnInterruptKeys.CtrlC, TurnInterruptKeys.Escape);

        await session.Terminal.WriteAsync(TurnInterruptKeys.Escape + rest);

        (await NextAsync(session, LongEnoughToBeSure)).Should().BeNull();
    }

    // The Codex shape: an agent whose keys have never been measured watches for none, and its cards
    // behave exactly as they did before this existed.
    [Fact]
    public async Task An_agent_that_named_no_keys_reports_nothing()
    {
        await using var session = Session();

        await session.Terminal.WriteAsync(TurnInterruptKeys.CtrlC);

        (await NextAsync(session, LongEnoughToBeSure)).Should().BeNull();
    }

    // A keystroke is not proof a session exists — a CLI parked on its trust prompt takes keys as
    // readily as a working one — so the interrupt must not disarm the startup watch, which is the
    // same reason terminal output is refused that job.
    [Fact]
    public async Task An_interrupt_does_not_disarm_the_startup_watch()
    {
        await using var session = new PtyAgentSession(
            Guid.NewGuid(),
            "s-1",
            process,
            new TestClock(),
            TimeSpan.FromMilliseconds(50),
            new TurnInterruptProfile([TurnInterruptKeys.CtrlC], []));

        await session.Terminal.WriteAsync(TurnInterruptKeys.CtrlC);

        (await NextAsync(session)).Should().BeOfType<TurnInterrupted>();
        (await NextAsync(session)).Should().BeOfType<StartupPromptWaiting>();
    }

    private PtyAgentSession Session(params string[] keys)
        => new(
            Guid.NewGuid(),
            "s-1",
            process,
            new TestClock(),
            TimeSpan.FromMinutes(1),
            keys.Length == 0 ? TurnInterruptProfile.None : new TurnInterruptProfile(keys, ['/', '@']));

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
