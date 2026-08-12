using Act.Core.Model;

namespace Act.Core.Abstractions;

public interface IUsageRefresher
{
    AgentType Agent { get; }

    Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default);
}
