namespace Act.Core.Abstractions;

// One live pseudo-terminal. `Output` is batched rather than raised per read: a full-screen
// TUI redraws far more often than a Blazor circuit wants to be poked, and a UTF-8 sequence
// can straddle two reads, so decoding is the implementation's job and never the caller's.
public interface IPtyProcess : IAsyncDisposable
{
    int ProcessId { get; }

    event Func<string, Task>? Output;

    event Action<int>? Exited;

    Task WriteAsync(string data, CancellationToken cancellationToken = default);

    // No `Kill`: disposal is the only teardown, and it kills the connection itself. A second way
    // to end a process was one an adapter could take without ACT retiring the session's token or
    // its config file, both of which hang off disposal.
    void Resize(int cols, int rows);
}
