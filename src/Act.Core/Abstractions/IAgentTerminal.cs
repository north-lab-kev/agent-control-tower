namespace Act.Core.Abstractions;

// The live terminal behind a session. ACT pipes these bytes and never reads them for
// meaning — state comes from the ingestion sources, never from the screen. `Backlog` is
// what a re-attaching view replays, so leaving a card's terminal and coming back does not
// show an empty one; it is bounded, so a redrawing TUI cannot grow it without limit.
//
// `WriteAsync` carries the user's own keystrokes, arriving from the xterm in their browser.
// There is no ACT-composed write beside it: send-back was cut on 2026-08-01, and with it the
// last thing ACT would have typed on the user's behalf. Everything the agent is told after
// launch, the user types.
public interface IAgentTerminal
{
    event Func<string, Task>? Output;

    string Backlog { get; }

    Task WriteAsync(string data, CancellationToken cancellationToken = default);

    void Resize(int cols, int rows);
}
