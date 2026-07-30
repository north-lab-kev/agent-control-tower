using Act.Agents.ClaudeCode;
using Act.Core.Abstractions;
using Act.TestSupport;

namespace Act.Agents.Tests;

public class ClaudeCodeAdapterContractTests : AgentAdapterContract
{
    protected override IAgentAdapter CreateAdapter()
        => new ClaudeCodeAdapter(new StubPtyHost(), new TestClock());
}
