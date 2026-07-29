using System.Collections.Concurrent;
using System.Threading.Channels;
using Act.Core.Abstractions;
using Act.Core.Events;

namespace Act.TestSupport;

// Stands in for a live agent turn: a scripted event stream out, a recorded input channel in.
// The pump only completes the stream when the script runs to its end, so kill and disposal
// stay distinguishable from a session that finished on its own.
public sealed class MockAgentSession : IAgentSession
{
    private readonly Channel<AgentEvent> events = Channel.CreateUnbounded<AgentEvent>();

    private readonly Channel<AgentInput> inputs = Channel.CreateUnbounded<AgentInput>();

    private readonly ConcurrentQueue<AgentInput> received = new();

    private readonly CancellationTokenSource pumpCancellation = new();

    private readonly IClock clock;

    private readonly Task pump;

    private bool stopped;

    internal MockAgentSession(string sessionId, AgentScript script, IClock clock)
    {
        SessionId = sessionId;
        this.clock = clock;
        pump = Task.Run(() => RunAsync(script, pumpCancellation.Token));
    }

    public string SessionId { get; }

    public IReadOnlyCollection<AgentInput> Received => received;

    public IAsyncEnumerable<AgentEvent> Events => events.Reader.ReadAllAsync();

    public Task RespondToPermissionAsync(
        string requestId,
        PermissionDecision decision,
        CancellationToken cancellationToken = default)
        => Record(new AgentInput(AgentInputKind.Permission, requestId, Decision: decision));

    public Task AnswerAsync(string requestId, string answer, CancellationToken cancellationToken = default)
        => Record(new AgentInput(AgentInputKind.Answer, requestId, answer));

    public Task SendAsync(string message, CancellationToken cancellationToken = default)
        => Record(new AgentInput(AgentInputKind.Message, Text: message));

    public Task InterruptAsync(CancellationToken cancellationToken = default)
        => Record(new AgentInput(AgentInputKind.Interrupt));

    public async Task KillAsync(CancellationToken cancellationToken = default)
    {
        await Record(new AgentInput(AgentInputKind.Kill));
        await StopPumpAsync();

        events.Writer.TryWrite(new SessionKilled(SessionId, clock.Now));
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
                else
                    await events.Writer.WriteAsync(step.Event!(SessionId, clock.Now), cancellationToken);
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

    private Task Record(AgentInput input)
    {
        received.Enqueue(input);
        inputs.Writer.TryWrite(input);

        return Task.CompletedTask;
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
