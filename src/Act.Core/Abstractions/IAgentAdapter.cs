using Act.Core.Model;

namespace Act.Core.Abstractions;

// One agent CLI behind one seam. The adapter owns everything agent-shaped — flags, the
// command line it spawns under the pseudo-terminal, the hook config it generates, how its raw
// payloads normalize — and hands back a normalized session. Adding an agent must not touch the
// core.
public interface IAgentAdapter
{
    AgentType Agent { get; }

    AgentCapabilities Capabilities { get; }

    LaunchConfigResolution Resolve(LaunchConfig config);

    // Where this CLI is on the machine, asked once at startup. The adapter owns the knowledge of
    // its own install shapes; the probe owns the filesystem access.
    AgentInstall Locate(IExecutableProbe probe);

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
