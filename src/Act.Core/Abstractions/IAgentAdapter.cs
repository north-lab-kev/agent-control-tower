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

    // One question ACT asks for itself, answered off to the side: no session, no terminal, no
    // hooks, no working directory of the user's, and the cheapest model the agent offers. Null when
    // there is no answer — the CLI is missing, refused, or took too long — because nothing ACT asks
    // for itself is worth surfacing an error the user did not ask a question to get.
    Task<string?> QueryAsync(
        AgentQueryRequest request,
        CancellationToken cancellationToken = default);
}
