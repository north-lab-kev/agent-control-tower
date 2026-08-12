using Act.Core.Model;

namespace Act.Core.Abstractions;

public interface IUsageDialect
{
    AgentType Agent { get; }

    string DefaultCredentialsPath();

    string DefaultEndpoint { get; }

    UsageToken Token(string credentials, DateTimeOffset now);

    AgentUsage? Parse(string response, DateTimeOffset takenAt);
}
