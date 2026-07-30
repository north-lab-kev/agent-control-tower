using Act.Core.Abstractions;
using Act.Core.Model;
using Act.TestSupport;

namespace Act.Agents.Tests;

// The same contract against a deliberately different-shaped agent: another `AgentType`, no
// reasoning effort, and only the two permission modes it can honour. Cheap insurance that
// the seam is not Claude-shaped, and it drives the rejection branches the default mock never
// reaches.
public class NarrowMockAgentAdapterTests : AgentAdapterContract
{
    protected override IAgentAdapter CreateAdapter() => new MockAgentAdapter(
        AgentType.Codex,
        new AgentCapabilities(
            [new AgentModel("narrow-model", "Narrow", [])],
            "narrow-model",
            new HashSet<PermissionMode> { PermissionMode.Default, PermissionMode.Bypass }));
}
