namespace Act.Core.Agents;

// How one agent's TUI wants text handed to it. Bracketed paste matters for the initial
// prompt: without it a multi-line prompt is read as a series of separate submissions, and
// the agent starts working on the first line before it has seen the rest.
public sealed record TerminalSubmitProfile(bool BracketedPaste, string SubmitSequence)
{
    public static TerminalSubmitProfile Default { get; } = new(true, "\r");

    public string Format(string text) => BracketedPaste
        ? $"[200~{text}[201~{SubmitSequence}"
        : text + SubmitSequence;
}
