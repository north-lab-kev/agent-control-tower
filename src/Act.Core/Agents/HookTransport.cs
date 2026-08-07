namespace Act.Core.Agents;

// The wire details both sides of the hook channel have to agree on. Here rather than in the
// endpoint implementation because the adapters write them into agent config and cannot reference
// infrastructure, and here rather than duplicated because a header name that drifts between the
// writer and the reader fails as a silent 401.
public static class HookTransport
{
    public const string TokenHeader = "x-act-hook-token";

    public const string RoutePrefix = "/hooks";

    public const string ClaudeRoute = $"{RoutePrefix}/claude";

    public const string CodexRoute = $"{RoutePrefix}/codex";
}
