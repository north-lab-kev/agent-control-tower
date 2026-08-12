using Act.Core.Abstractions;

namespace Act.App.Sessions;

// The last geometry a real xterm reported, so a headless launch spawns at a size the user's window
// actually is. A pty must be sized at spawn and the agent paints to that size immediately; starting
// every unattended card at the 120×30 constant means the first thing anyone sees when they open it
// is a TUI reflowing itself.
//
// In memory only: after a restart there is no window to have measured, and the constant is the
// right answer again.
public sealed class TerminalGeometry
{
    private TerminalSize last = TerminalSize.Default;

    public TerminalSize Last => last;

    public void Report(TerminalSize size)
    {
        if (size.Cols > 0 && size.Rows > 0)
            last = size;
    }
}
