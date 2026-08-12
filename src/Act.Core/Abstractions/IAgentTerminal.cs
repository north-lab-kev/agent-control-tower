namespace Act.Core.Abstractions;

// The live terminal behind a session. ACT pipes these bytes and never reads them for
// meaning — state comes from the ingestion sources, never from the screen. `Backlog` is
// what a re-attaching view replays, so leaving a card's terminal and coming back does not
// show an empty one; it is bounded, so a redrawing TUI cannot grow it without limit.
//
// `WriteAsync` carries the user's own keystrokes, arriving from the xterm in their browser.
// ACT composes no *instruction* beside them: what the agent is told after launch, the user says.
//
// The one write ACT authors is a dropped file's own path, inserted where the cursor is and never
// followed by a submit key — the same completion of a drag gesture every terminal emulator
// performs, not ACT answering on the user's behalf: nothing is phrased, nothing is
// decided, and nothing is sent until the user presses Enter.
public interface IAgentTerminal
{
    event Func<string, Task>? Output;

    string Backlog { get; }

    Task WriteAsync(string data, CancellationToken cancellationToken = default);

    void Resize(int cols, int rows);
}
