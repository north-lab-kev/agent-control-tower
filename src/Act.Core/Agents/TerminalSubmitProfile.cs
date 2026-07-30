namespace Act.Core.Agents;

// How one agent's TUI wants text handed to it. Bracketed paste matters for the initial
// prompt: without it a multi-line prompt is read as a series of separate submissions, and
// the agent starts working on the first line before it has seen the rest.
//
// The submit key is deliberately *not* part of the paste. A carriage return arriving in the same
// burst is consumed with the pasted block and lands in the composer as one more newline — the
// prompt sits there typed and never sent. It has to arrive as its own keystroke, a moment later,
// which is what `SubmitDelay` buys.
public sealed record TerminalSubmitProfile(bool BracketedPaste, string SubmitSequence, TimeSpan SubmitDelay)
{
    public static TerminalSubmitProfile Default { get; } =
        new(true, "\r", TimeSpan.FromMilliseconds(300));

    public string Paste(string text) => BracketedPaste
        ? $"[200~{text}[201~"
        : text;
}
