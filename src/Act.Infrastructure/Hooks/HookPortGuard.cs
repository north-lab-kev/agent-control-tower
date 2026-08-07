using Act.Core.Agents;

namespace Act.Infrastructure.Hooks;

// The rule that makes two listeners on one host mean two different things: the agent-facing routes
// answer only on the hook port, everything else only off it. Both directions matter — the second is
// what stops that surface from being reachable on the port the browser (and one day a phone) talks
// to.
//
// Two families are agent-facing, not one: the hook posts, and the MCP server. They share the port and
// the token for the same reason they share this rule — an agent's whole conversation with ACT belongs
// on the address the agent was configured with, and nowhere else.
//
// A plain string rather than ASP.NET's `PathString`, so the rule stays testable here and this whole
// project stays free of a web framework reference for the sake of one middleware.
public static class HookPortGuard
{
    public static bool Rejects(int localPort, int hookPort, string path)
    {
        // Until the endpoint has bound a port, no request can be on it, and treating every path as
        // misrouted would take the whole UI down with it.
        if (hookPort <= 0)
            return IsAgentPath(path);

        return (localPort == hookPort) != IsAgentPath(path);
    }

    private static bool IsAgentPath(string path)
        => path.StartsWith(HookTransport.RoutePrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(McpTransport.Route, StringComparison.OrdinalIgnoreCase);
}
