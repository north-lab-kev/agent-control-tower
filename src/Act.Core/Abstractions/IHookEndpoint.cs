using Act.Core.Model;

namespace Act.Core.Abstractions;

// The localhost endpoint every ACT-launched agent posts its hooks to. One endpoint for every
// session and both agents: the port is not the boundary — any local process can enumerate
// listening ports — so it buys collision-avoidance, and the per-session token does the
// authorizing. Kept off the app's own port because that one is not ACT's to pin (the packaged
// Electron shell decides it), and a hook url that moves between launches re-triggers Codex's
// hook-review prompt every time.
public interface IHookEndpoint
{
    // Null until the endpoint is listening, which is the honest answer for an adapter deciding
    // whether to write hook config at all: no endpoint, no hooks, launch anyway.
    Uri? BaseAddress { get; }

    Uri? UrlFor(AgentType agent);

    // The MCP server's url — the same loopback endpoint as the hooks, at its own route — derived
    // here once so the two adapters' generated config cannot disagree about it. Null exactly when
    // `BaseAddress` is.
    Uri? McpUrl { get; }

    // Issued per session, carried on the agent process environment rather than in a hook
    // command, so Codex's definition hash stays stable across launches.
    string Register(Guid taskId);

    bool TryResolve(string token, out Guid taskId);

    void Release(Guid taskId);
}
