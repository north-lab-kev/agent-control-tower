using System.Text;
using Act.Core.Abstractions;

namespace Act.Core.Agents;

// The terminal half of a PTY-hosted session. It keeps its own bounded scrollback rather than
// leaning on the view's, because the view comes and goes — a card's terminal has to look the
// same when you walk back into it as when you left. The buffer is trimmed from the front, so
// a long-running session loses its oldest output rather than its most recent.
public sealed class PtyAgentTerminal(IPtyProcess process, TerminalSubmitProfile submitProfile)
    : IAgentTerminal
{
    private const int BacklogCeiling = 256 * 1024;

    private readonly StringBuilder backlog = new();

    private readonly Lock gate = new();

    public event Func<string, Task>? Output;

    public string Backlog
    {
        get
        {
            lock (gate)
                return backlog.ToString();
        }
    }

    public Task WriteAsync(string data, CancellationToken cancellationToken = default)
        => process.WriteAsync(data, cancellationToken);

    public Task SubmitAsync(string text, CancellationToken cancellationToken = default)
        => process.WriteAsync(submitProfile.Format(text), cancellationToken);

    public void Resize(int cols, int rows) => process.Resize(cols, rows);

    internal async Task OnProcessOutputAsync(string chunk)
    {
        lock (gate)
        {
            backlog.Append(chunk);

            if (backlog.Length > BacklogCeiling)
                backlog.Remove(0, backlog.Length - BacklogCeiling);
        }

        if (Output is { } handler)
            await handler(chunk);
    }
}
