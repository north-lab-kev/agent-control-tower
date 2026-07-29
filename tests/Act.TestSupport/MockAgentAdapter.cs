using Act.Core.Abstractions;
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
        [FastModel, DeepModel],
        FastModel,
        ["low", "high"],
        new HashSet<PermissionMode>(Enum.GetValues<PermissionMode>()));

    public LaunchConfigResolution Resolve(LaunchConfig config)
    {
        var resolved = config.Copy();
        var adjustments = new List<LaunchConfigAdjustment>();
        var rejections = new List<string>();

        if (resolved.Model is null)
            resolved.Model = Capabilities.DefaultModel;
        else if (!Capabilities.Models.Contains(resolved.Model))
            rejections.Add($"Model '{resolved.Model}' is not available for {Agent}.");

        // Effort is advisory wherever it exists, so an unknown value is worth substituting
        // rather than refusing to launch over. An agent with no effort list does not model
        // effort at all and hands the value straight through.
        if (Capabilities.Efforts.Count > 0
            && resolved.Effort is { } effort
            && !Capabilities.Efforts.Contains(effort))
        {
            var substitute = Capabilities.Efforts[^1];

            adjustments.Add(new LaunchConfigAdjustment(
                nameof(LaunchConfig.Effort),
                effort,
                substitute,
                $"'{effort}' is not one of {Agent}'s reasoning efforts."));

            resolved.Effort = substitute;
        }

        if (!Capabilities.PermissionModes.Contains(resolved.PermissionMode))
            rejections.Add($"Permission mode '{resolved.PermissionMode}' is not supported by {Agent}.");

        return new LaunchConfigResolution(resolved, adjustments, rejections);
    }

    public Task<IAgentSession> LaunchAsync(
        AgentLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        Refuse(request.Config);
        Launches.Add(request);

        return Task.FromResult<IAgentSession>(new MockAgentSession(request.SessionId, Script, clock));
    }

    public Task<IAgentSession> ResumeAsync(
        AgentResumeRequest request,
        CancellationToken cancellationToken = default)
    {
        Refuse(request.Config);
        Resumes.Add(request);

        return Task.FromResult<IAgentSession>(new MockAgentSession(request.SessionId, Script, clock));
    }

    private void Refuse(LaunchConfig config)
    {
        var resolution = Resolve(config);
        if (resolution.CanLaunch)
            return;

        throw new InvalidOperationException(string.Join(" ", resolution.Rejections));
    }
}
