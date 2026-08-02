using System.Threading.Channels;
using Act.Core.Abstractions;
using Act.Core.Events;

namespace Act.Core.Agents;

// A session hosted in a pseudo-terminal. Agent-agnostic on purpose: everything agent-shaped
// (the command line, the submit profile) is decided by the adapter and handed in, so both
// adapters share this rather than each growing their own. Observed events reach it by push
// through `Publish`, whoever saw them. It depends on ports only, which is why it can live in
// the core at all.
public sealed class PtyAgentSession : IAgentSession
{
    // How long a launch may stay silent before ACT calls it parked. Measured against `claude-code`
    // on 2026-07-30: a directory the CLI already trusts produces its first hook ~0.8 s after the
    // spawn, so this is an order of magnitude of headroom. Overshooting costs a slow start two
    // extra transitions and nothing else — the first real hook is activity and puts the card back.
    public static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(8);

    private readonly Channel<AgentEvent> events = Channel.CreateUnbounded<AgentEvent>();

    // Cancelled by the first thing that proves the CLI got past its pre-session prompt, or by the
    // session ending before it ever did.
    private readonly CancellationTokenSource startupWatch = new();

    private readonly PtyAgentTerminal terminal;

    private readonly IPtyProcess process;

    private readonly IClock clock;

    private bool disposed;

    public PtyAgentSession(
        Guid taskId,
        string? sessionId,
        IPtyProcess process,
        IClock clock,
        TimeSpan? startupGrace = null)
    {
        TaskId = taskId;
        SessionId = sessionId;
        this.process = process;
        this.clock = clock;
        terminal = new PtyAgentTerminal(process);

        process.Output += terminal.OnProcessOutputAsync;
        process.Exited += OnProcessExited;

        var grace = startupGrace ?? StartupGrace;

        if (grace > TimeSpan.Zero)
            _ = WatchStartupAsync(grace);
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

    // Any hook at all is proof the session exists, so it is what disarms the startup watch — not
    // terminal output, which a CLI sitting on its trust prompt produces just as freely as a working
    // one.
    public void Publish(AgentEvent agentEvent)
    {
        startupWatch.Cancel();

        events.Writer.TryWrite(agentEvent);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;

        process.Output -= terminal.OnProcessOutputAsync;
        process.Exited -= OnProcessExited;

        // Cancelled, not disposed: `Publish` and the exit handler both disarm it, and either can
        // still be racing a dispose. A source with no timer and no linked token costs nothing to
        // leave to the collector; an `ObjectDisposedException` out of a hook would cost an event.
        await startupWatch.CancelAsync();

        await process.DisposeAsync();

        events.Writer.TryComplete();
    }

    // Only reachable while the session is still bound: `DisposeAsync` unsubscribes before it tears
    // the process down, so a teardown ACT asked for — a sign-off, a terminal restart — never comes
    // back as an exit the card would wear as `error`.
    private void OnProcessExited(int exitCode)
    {
        startupWatch.Cancel();

        events.Writer.TryWrite(new ProcessExited(SessionId ?? string.Empty, clock.Now, exitCode));
        events.Writer.TryComplete();
    }

    // The pre-session prompt has no hook behind it, so silence is what reports it. One shot: the
    // card is in Your turn from here and the next thing the CLI does moves it, whatever that is.
    private async Task WatchStartupAsync(TimeSpan grace)
    {
        try
        {
            await Task.Delay(grace, startupWatch.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        events.Writer.TryWrite(new StartupPromptWaiting(SessionId ?? string.Empty, clock.Now));
    }
}
