using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Core.Tests;

// The terminal keeps its own scrollback because the *view* comes and goes: a card's terminal has to
// look the same when you walk back into it as when you left. Everything else is a pass-through to the
// process, and the pass-through is what these pin — a `Resize` that never reached the pty is a TUI
// drawing at the wrong width for the life of the session.
//
// Driven through a real `PtyAgentSession` rather than by constructing the terminal directly: the
// session is what subscribes the terminal to the process, so a bare terminal receives nothing and a
// test of one would only prove the constructor runs. No startup grace, so nothing writes a
// `StartupPromptWaiting` into the stream while a test is reading it.
public class PtyAgentTerminalTests
{
    private readonly StubPtyProcess process = new();

    [Fact]
    public void A_fresh_terminal_has_nothing_to_replay()
        => Terminal().Backlog.Should().BeEmpty();

    [Fact]
    public async Task Output_the_process_produced_is_what_a_returning_view_replays()
    {
        var terminal = Terminal();

        await process.EmitAsync("first ");
        await process.EmitAsync("second");

        terminal.Backlog.Should().Be("first second");
    }

    [Fact]
    public async Task Output_is_forwarded_to_whoever_is_watching()
    {
        var terminal = Terminal();
        var seen = new List<string>();

        terminal.Output += chunk =>
        {
            seen.Add(chunk);

            return Task.CompletedTask;
        };

        await process.EmitAsync("hello");

        seen.Should().Equal("hello");
    }

    // The backlog is what a re-attaching view replays, so it has to hold what arrived *before* anyone
    // was watching as well as after.
    [Fact]
    public async Task Output_that_arrived_before_anyone_was_watching_is_still_replayed()
    {
        var terminal = Terminal();

        await process.EmitAsync("while nobody looked");

        var seen = new List<string>();

        terminal.Output += chunk =>
        {
            seen.Add(chunk);

            return Task.CompletedTask;
        };

        terminal.Backlog.Should().Be("while nobody looked");
        seen.Should().BeEmpty("a late subscriber is handed the backlog, not the history as events");
    }

    [Fact]
    public async Task A_write_is_handed_straight_to_the_process()
    {
        await Terminal().WriteAsync("ls -la\r");

        process.Writes.Should().Equal("ls -la\r");
    }

    [Fact]
    public void A_resize_is_handed_straight_to_the_process()
    {
        Terminal().Resize(120, 40);

        process.Resizes.Should().Equal(new TerminalSize(120, 40));
    }

    // Trimmed from the front, so a long-running session loses its oldest output rather than its most
    // recent — the opposite would make the pane useless exactly when it matters.
    [Fact]
    public async Task A_session_that_never_stops_talking_keeps_its_newest_output()
    {
        var terminal = Terminal();

        for (var chunk = 0; chunk < 40; chunk++)
            await process.EmitAsync(new string('x', 16 * 1024));

        await process.EmitAsync("the last thing it said");

        terminal.Backlog.Should().EndWith("the last thing it said");
        terminal.Backlog.Length.Should().BeLessThan(400 * 1024, "the buffer is bounded");
    }

    private IAgentTerminal Terminal()
        => new PtyAgentSession(
                Guid.NewGuid(),
                "s-1",
                process,
                new TestClock(),
                startupGrace: TimeSpan.Zero)
            .Terminal;
}
