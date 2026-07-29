using Act.Core.Model;

namespace Act.Core.Abstractions;

// One agent CLI behind one seam. The adapter owns everything agent-shaped — flags, the
// command line it spawns under the pseudo-terminal, which ingestion sources it composes,
// how the preamble is injected — and hands back a normalized session. Adding an agent must
// not touch the core.
public interface IAgentAdapter
{
    AgentType Agent { get; }

    AgentCapabilities Capabilities { get; }

    LaunchConfigResolution Resolve(LaunchConfig config);

    // Null for an agent with no desktop app, which is what keeps `claude://resume?session=`
    // knowledge inside the Claude adapter instead of leaking into the UI.
    string? DesktopHandoffUrl(string sessionId, string workingDir);

    Task<IAgentSession> LaunchAsync(
        AgentLaunchRequest request,
        CancellationToken cancellationToken = default);

    Task<IAgentSession> ResumeAsync(
        AgentResumeRequest request,
        CancellationToken cancellationToken = default);
}
