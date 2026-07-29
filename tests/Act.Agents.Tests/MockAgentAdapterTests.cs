using Act.Core.Abstractions;
using Act.TestSupport;

namespace Act.Agents.Tests;

public class MockAgentAdapterTests : AgentAdapterContract
{
    protected override IAgentAdapter CreateAdapter() => new MockAgentAdapter();
}
