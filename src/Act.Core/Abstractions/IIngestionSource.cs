using Act.Core.Events;

namespace Act.Core.Abstractions;

// One transport an adapter can compose into a session's event stream: the control stream,
// an HTTP hook endpoint, a file watcher, process supervision. Each normalizes on its own
// side of this interface, which is why the rules engine never learns how an event arrived.
public interface IIngestionSource : IAsyncDisposable
{
    IAsyncEnumerable<AgentEvent> ReadAsync(CancellationToken cancellationToken = default);
}
