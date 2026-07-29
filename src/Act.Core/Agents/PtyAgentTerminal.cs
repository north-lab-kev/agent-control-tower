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

    private const int PollMilliseconds = 50;

    private static readonly TimeSpan PaintTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SettleCeiling = TimeSpan.FromSeconds(3);

    private readonly TaskCompletionSource painted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly StringBuilder backlog = new();

    private readonly Lock gate = new();

    private long lastOutputAt;

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

    // Two writes with waits around them, and every part of that earns its place. A CLI is not
    // listening the instant it is spawned, so the text goes in once it has painted and gone quiet.
    // The submit key is then its own write: a carriage return arriving in the same burst as the
    // paste is consumed with it and lands in the composer as one more newline — which is exactly
    // the "prompt is there, complete, and was never sent" this waits to avoid.
    public async Task SubmitAsync(string text, CancellationToken cancellationToken = default)
    {
        await PaintedAsync(cancellationToken);
        await SettledAsync(cancellationToken);

        await process.WriteAsync(submitProfile.Paste(text), cancellationToken);

        await SettledAsync(cancellationToken);

        await process.WriteAsync(submitProfile.SubmitSequence, cancellationToken);
    }

    public void Resize(int cols, int rows) => process.Resize(cols, rows);

    internal async Task OnProcessOutputAsync(string chunk)
    {
        Interlocked.Exchange(ref lastOutputAt, Environment.TickCount64);

        painted.TrySetResult();

        lock (gate)
        {
            backlog.Append(chunk);

            if (backlog.Length > BacklogCeiling)
                backlog.Remove(0, backlog.Length - BacklogCeiling);
        }

        if (Output is { } handler)
            await handler(chunk);
    }

    // First output is the readiness signal ACT is allowed to use: it says the process has started
    // painting, not what is on the screen — reading the screen for meaning is what the ingestion
    // sources are for. Capped, so an agent that paints nothing swallows the prompt rather than
    // holding it forever.
    private async Task PaintedAsync(CancellationToken cancellationToken)
    {
        if (painted.Task.IsCompleted)
            return;

        try
        {
            await painted.Task.WaitAsync(PaintTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
        }
    }

    // Quiet, not elapsed: a fixed pause is a guess about how long a machine takes to draw a TUI,
    // and it is wrong on both ends. Its own ceiling, separate from the paint one, because a screen
    // that never stops repainting must not postpone the prompt indefinitely.
    private async Task SettledAsync(CancellationToken cancellationToken)
    {
        var quiet = (long)submitProfile.SubmitDelay.TotalMilliseconds;
        var deadline = Environment.TickCount64 + (long)SettleCeiling.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (Environment.TickCount64 - Interlocked.Read(ref lastOutputAt) >= quiet)
                return;

            await Task.Delay(PollMilliseconds, cancellationToken);
        }
    }
}
