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

    public AgentScript Script { get; set; } = AgentScript.Start().EndsTurn();

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
        Enum.GetValues<PermissionMode>(),
        DesktopHandoff: true);

    public string? DesktopHandoffUrl(string sessionId, string workingDir)
        => Capabilities.DesktopHandoff ? $"mock://resume?session={sessionId}" : null;

    public LaunchConfigResolution Resolve(LaunchConfig config)
        => LaunchConfigResolver.Resolve(Agent, Capabilities, config);

    // Whatever a test needs discovery to have found. On PATH by default, which is the shape that
    // makes ACT change nothing.
    public AgentInstall Install { get; set; } = AgentInstall.OnPath;

    // A spawn that throws — a binary that moved, a working directory that vanished, a transcript the
    // CLI refused to resume. Distinct from a refused *resolution*, which never reaches the adapter:
    // this is the failure that happens after ACT has committed to starting something.
    public Exception? Fails { get; set; }

    public AgentInstall Locate(IExecutableProbe probe) => Install;

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

    // Whatever a test needs a query to come back with. Null is the failure shape — a CLI that is not
    // there — and it is the one every caller has to survive, so it stays easy to ask for.
    public string? Answer { get; set; } = "Mock title";

    public List<AgentQueryRequest> Queries { get; } = [];

    // Separate from `Fails`, which is about a spawn: a query has a failure of its own — a binary that
    // is not there at all — and its callers are supposed to survive that rather than propagate it.
    public Exception? QueryFails { get; set; }

    // A query a test can hold open, so whatever is waiting on it can be looked at mid-flight — a
    // card's pending-title state only exists while the one-shot title query runs.
    public TaskCompletionSource? QueryHeld { get; set; }

    public async Task<string?> QueryAsync(
        AgentQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        Queries.Add(request);

        if (QueryHeld is { } held)
            await held.Task;

        if (QueryFails is { } failure)
            throw failure;

        return Answer;
    }

    private void Refuse(LaunchConfig config)
    {
        if (Fails is { } failure)
            throw failure;

        var resolution = Resolve(config);
        if (resolution.CanLaunch)
            return;

        throw new InvalidOperationException(string.Join(" ", resolution.Rejections));
    }
}
