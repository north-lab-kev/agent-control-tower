using System.Text;
using Act.Core.Abstractions;

namespace Act.TestSupport;

// A terminal with no pseudo-terminal behind it: writes are recorded so a test can assert
// what ACT typed, and scripted output is pushed through the same batching contract a real
// one has — including accumulating into `Backlog` while nothing is attached, which is what
// lets a re-attaching view replay the screen.
public sealed class MockAgentTerminal(Action<AgentInput> record) : IAgentTerminal
{
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
    {
        record(new AgentInput(AgentInputKind.Write, data));

        return Task.CompletedTask;
    }

    public void Resize(int cols, int rows)
        => record(new AgentInput(AgentInputKind.Resize, Size: new TerminalSize(cols, rows)));

    internal async Task EmitAsync(string chunk)
    {
        lock (gate)
            backlog.Append(chunk);

        if (Output is { } handler)
            await handler(chunk);
    }
}
