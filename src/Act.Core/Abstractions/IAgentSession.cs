using Act.Core.Events;

namespace Act.Core.Abstractions;

// A live agent turn. Disposal is the teardown between engagements: ACT keeps a session
// alive while a turn is running and lets work continue later through the adapter's resume,
// so a disposed session must complete its event stream rather than merely stop yielding.
public interface IAgentSession : IAsyncDisposable
{
    string SessionId { get; }

    IAsyncEnumerable<AgentEvent> Events { get; }

    Task RespondToPermissionAsync(
        string requestId,
        PermissionDecision decision,
        CancellationToken cancellationToken = default);

    Task AnswerAsync(string requestId, string answer, CancellationToken cancellationToken = default);

    Task SendAsync(string message, CancellationToken cancellationToken = default);

    Task InterruptAsync(CancellationToken cancellationToken = default);

    Task KillAsync(CancellationToken cancellationToken = default);
}
