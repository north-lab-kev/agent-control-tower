namespace Act.Core.Abstractions;

// The live terminal behind a session. ACT pipes these bytes and never reads them for
// meaning — state comes from the ingestion sources, never from the screen. `Backlog` is
// what a re-attaching view replays, so leaving a card's terminal and coming back does not
// show an empty one; it is bounded, so a redrawing TUI cannot grow it without limit.
public interface IAgentTerminal
{
    event Func<string, Task>? Output;

    string Backlog { get; }

    Task WriteAsync(string data, CancellationToken cancellationToken = default);

    // The only two things ACT types itself: the initial prompt and a send-back message.
    // Adapter-implemented because bracketed paste and the submit key are agent-shaped.
    Task SubmitAsync(string text, CancellationToken cancellationToken = default);

    void Resize(int cols, int rows);
}
