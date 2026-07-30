namespace Act.Infrastructure.Hooks;

// The rule that makes two listeners on one host mean two different things: hooks answer only on the
// hook port, everything else only off it. Both directions matter — the second is what stops the
// hook endpoint from being reachable on the port the browser (and one day a phone) talks to.
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
            return IsHookPath(path);

        return (localPort == hookPort) != IsHookPath(path);
    }

    private static bool IsHookPath(string path)
        => path.StartsWith(HookEndpoint.RoutePrefix, StringComparison.OrdinalIgnoreCase);
}
