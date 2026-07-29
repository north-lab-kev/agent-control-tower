using System.Threading.Channels;
using Act.Core.Abstractions;
using Act.Core.Events;

namespace Act.Core.Agents;

// A session hosted in a pseudo-terminal. Agent-agnostic on purpose: everything agent-shaped
// (the command line, the submit profile, which ingestion sources to compose) is decided by
// the adapter and handed in, so both adapters share this rather than each growing their own.
// It depends on ports only, which is why it can live in the core at all.
public sealed class PtyAgentSession : IAgentSession
{
    private readonly Channel<AgentEvent> events = Channel.CreateUnbounded<AgentEvent>();

    private readonly PtyAgentTerminal terminal;

    private readonly IPtyProcess process;

    private readonly IClock clock;

    private bool killed;

    private bool disposed;

    public PtyAgentSession(
        Guid taskId,
        string? sessionId,
        IPtyProcess process,
        TerminalSubmitProfile submitProfile,
        IClock clock)
    {
        TaskId = taskId;
        SessionId = sessionId;
        this.process = process;
        this.clock = clock;
        terminal = new PtyAgentTerminal(process, submitProfile);

        process.Output += terminal.OnProcessOutputAsync;
        process.Exited += OnProcessExited;
    }

    public Guid TaskId { get; }

    public string? SessionId { get; private set; }

    public IAgentTerminal Terminal => terminal;

    public IAsyncEnumerable<AgentEvent> Events => events.Reader.ReadAllAsync();

    public int ProcessId => process.ProcessId;

    // How a reported-id agent's binding lands: the first `SessionStart` payload names the
    // session, and everything after that correlates on it instead of on the task id.
    public void BindSessionId(string sessionId)
    {
        if (SessionId is not null)
            return;

        SessionId = sessionId;
    }

    public void Publish(AgentEvent agentEvent) => events.Writer.TryWrite(agentEvent);

    // Deliberately not awaited by the caller: the opening prompt cannot be typed until the CLI's
    // TUI is up, and holding the launch open for however long that takes would leave the board
    // waiting on a paint. A failure here shows as an agent sitting at an empty prompt, which the
    // terminal makes plain — there is nothing better ACT could report from inside a keystroke.
    public void Open(string text) => _ = OpenAsync(text);

    private async Task OpenAsync(string text)
    {
        try
        {
            await terminal.SubmitAsync(text);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException
            or OperationCanceledException)
        {
        }
    }

    public async Task KillAsync(CancellationToken cancellationToken = default)
    {
        if (killed)
            return;

        killed = true;

        await process.KillAsync(cancellationToken);

        events.Writer.TryWrite(new SessionKilled(SessionId ?? string.Empty, clock.Now));
        events.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;

        process.Output -= terminal.OnProcessOutputAsync;
        process.Exited -= OnProcessExited;

        await process.DisposeAsync();

        events.Writer.TryComplete();
    }

    // A kill is a user action and already reported itself; reporting the exit as well would
    // hand the card an `error` badge for something it asked for.
    private void OnProcessExited(int exitCode)
    {
        if (killed)
            return;

        events.Writer.TryWrite(new ProcessExited(SessionId ?? string.Empty, clock.Now, exitCode));
        events.Writer.TryComplete();
    }
}
