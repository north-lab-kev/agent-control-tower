using Act.Core.Model;

namespace Act.Core.Abstractions;

public interface IUsageProbe
{
    AgentType Agent { get; }

    Task<AgentUsage?> ReadAsync(CancellationToken cancellationToken = default);
}
