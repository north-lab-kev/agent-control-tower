using System.Collections.Concurrent;
using System.Threading.Channels;
using Act.Core.Abstractions;
using Act.Core.Events;

namespace Act.TestSupport;

// Stands in for a live agent session: a scripted event stream out, a recorded terminal in.
// The pump only completes the stream when the script runs to its end, so kill and disposal
// stay distinguishable from a session that finished on its own.
public sealed class MockAgentSession : IAgentSession
{
    private readonly Channel<AgentEvent> events = Channel.CreateUnbounded<AgentEvent>();

    private readonly Channel<AgentInput> inputs = Channel.CreateUnbounded<AgentInput>();

    private readonly ConcurrentQueue<AgentInput> received = new();

    private readonly CancellationTokenSource pumpCancellation = new();

    private readonly MockAgentTerminal terminal;

    private readonly IClock clock;

    private readonly Task pump;

    private bool stopped;

    internal MockAgentSession(Guid taskId, string? sessionId, AgentScript script, IClock clock)
    {
        TaskId = taskId;
        SessionId = sessionId;
        this.clock = clock;
        terminal = new MockAgentTerminal(Record);
        pump = Task.Run(() => RunAsync(script, pumpCancellation.Token));
    }

    public Guid TaskId { get; }

    public string? SessionId { get; private set; }

    public void BindSessionId(string sessionId) => SessionId ??= sessionId;

    public IAgentTerminal Terminal => terminal;

    public IReadOnlyCollection<AgentInput> Received => received;

    public IAsyncEnumerable<AgentEvent> Events => events.Reader.ReadAllAsync();

    public async Task KillAsync(CancellationToken cancellationToken = default)
    {
        Record(new AgentInput(AgentInputKind.Kill));
        await StopPumpAsync();

        events.Writer.TryWrite(new SessionKilled(SessionId ?? string.Empty, clock.Now));
        events.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        await StopPumpAsync();

        events.Writer.TryComplete();
    }

    private async Task RunAsync(AgentScript script, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var step in script.Steps)
            {
                if (step.Awaits is { } kind)
                    await WaitForAsync(kind, cancellationToken);
                else if (step.TerminalOutput is { } chunk)
                    await terminal.EmitAsync(chunk);
                else
                    await events.Writer.WriteAsync(step.Event!(SessionId ?? string.Empty, clock.Now), cancellationToken);
            }

            events.Writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WaitForAsync(AgentInputKind kind, CancellationToken cancellationToken)
    {
        while (true)
        {
            var input = await inputs.Reader.ReadAsync(cancellationToken);
            if (input.Kind == kind)
                return;
        }
    }

    private void Record(AgentInput input)
    {
        received.Enqueue(input);
        inputs.Writer.TryWrite(input);
    }

    private async Task StopPumpAsync()
    {
        if (stopped)
            return;

        stopped = true;

        await pumpCancellation.CancelAsync();
        await pump;

        pumpCancellation.Dispose();
    }
}
