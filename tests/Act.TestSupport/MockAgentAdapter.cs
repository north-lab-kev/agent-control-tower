using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;

namespace Act.TestSupport;

// The lifecycle fixture the whole build leans on: a full adapter with no CLI behind it.
// It reports whichever `AgentType` it is constructed for, so the same script can be run as
// Claude Code and as Codex — the cheapest guard against a Claude-shaped seam.
public sealed class MockAgentAdapter(
    AgentType agent = AgentType.ClaudeCode,
    AgentCapabilities? capabilities = null,
    IClock? clock = null) : IAgentAdapter
{
    public const string FastModel = "mock-fast";

    public const string DeepModel = "mock-deep";

    private readonly IClock clock = clock ?? new TestClock();

    public AgentType Agent { get; } = agent;

    public AgentCapabilities Capabilities { get; } = capabilities ?? DefaultCapabilities();

    public AgentScript Script { get; set; } = AgentScript.Start().EndsTurn(TurnOutcome.ReadyForReview);

    public List<AgentLaunchRequest> Launches { get; } = [];

    public List<AgentResumeRequest> Resumes { get; } = [];

    public static AgentCapabilities DefaultCapabilities() => new(
        [
            new AgentModel(FastModel, "Mock Fast", ["low", "high"], "low"),

            // A second ladder on purpose: it is what keeps the per-model effort rule honest,
            // since a flat list would pass every test a single-ladder agent could write.
            new AgentModel(DeepModel, "Mock Deep", ["low", "high", "max"], "high"),
        ],
        FastModel,
        new HashSet<PermissionMode>(Enum.GetValues<PermissionMode>()),
        DesktopHandoff: true);

    public string? DesktopHandoffUrl(string sessionId, string workingDir)
        => Capabilities.DesktopHandoff ? $"mock://resume?session={sessionId}" : null;

    public LaunchConfigResolution Resolve(LaunchConfig config)
        => LaunchConfigResolver.Resolve(Agent, Capabilities, config);

    public Task<IAgentSession> LaunchAsync(
        AgentLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        Refuse(request.Config);
        Launches.Add(request);

        return Task.FromResult<IAgentSession>(
            new MockAgentSession(request.TaskId, request.SessionId, Script, clock));
    }

    public Task<IAgentSession> ResumeAsync(
        AgentResumeRequest request,
        CancellationToken cancellationToken = default)
    {
        Refuse(request.Config);
        Resumes.Add(request);

        return Task.FromResult<IAgentSession>(
            new MockAgentSession(request.TaskId, request.SessionId, Script, clock));
    }

    private void Refuse(LaunchConfig config)
    {
        var resolution = Resolve(config);
        if (resolution.CanLaunch)
            return;

        throw new InvalidOperationException(string.Join(" ", resolution.Rejections));
    }
}
