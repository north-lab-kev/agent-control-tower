using Act.Core.Abstractions;

namespace Act.TestSupport;

// Lets the real adapters be constructed and their command lines asserted without a CLI on the
// machine — which is the point of `IPtyHost` being a port. It records what would have been
// spawned; nothing is ever executed.
public sealed class StubPtyHost : IPtyHost
{
    public List<PtyStartInfo> Started { get; } = [];

    public PtyStartInfo Last => Started[^1];

    public StubPtyProcess LastProcess { get; private set; } = new();

    public Task<IPtyProcess> StartAsync(
        PtyStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        Started.Add(startInfo);
        LastProcess = new StubPtyProcess();

        return Task.FromResult<IPtyProcess>(LastProcess);
    }
}

public sealed class StubPtyProcess : IPtyProcess
{
    public List<string> Writes { get; } = [];

    public List<TerminalSize> Resizes { get; } = [];


    public int ProcessId => 4242;

#pragma warning disable CS0067
    public event Func<string, Task>? Output;

    public event Action<int>? Exited;
#pragma warning restore CS0067

    public Task WriteAsync(string data, CancellationToken cancellationToken = default)
    {
        Writes.Add(data);

        return Task.CompletedTask;
    }

    public void Resize(int cols, int rows) => Resizes.Add(new TerminalSize(cols, rows));

    public async Task EmitAsync(string chunk)
    {
        if (Output is { } handler)
            await handler(chunk);
    }

    public void Exit(int exitCode) => Exited?.Invoke(exitCode);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
