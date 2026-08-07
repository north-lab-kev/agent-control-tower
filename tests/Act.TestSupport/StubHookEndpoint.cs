using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.TestSupport;

// A hook endpoint that is listening but has no server behind it, so adapter tests can assert what
// a launch writes and injects. Construct with `Offline()` for the other case that must keep
// working: no endpoint, no hook config, launch anyway.
public sealed class StubHookEndpoint : IHookEndpoint
{
    private readonly Dictionary<Guid, string> tokens = [];

    private int issued;

    public const int Port = 49711;

    public StubHookEndpoint(int port = Port)
        => BaseAddress = port > 0 ? new Uri($"http://127.0.0.1:{port}") : null;

    public Uri? BaseAddress { get; }

    public List<Guid> Released { get; } = [];

    public static StubHookEndpoint Offline() => new(0);

    public Uri? UrlFor(AgentType agent) => BaseAddress is null
        ? null
        : new Uri(BaseAddress, agent is AgentType.ClaudeCode
            ? HookTransport.ClaudeRoute
            : HookTransport.CodexRoute);

    public Uri? McpUrl => BaseAddress is null ? null : new Uri(BaseAddress, McpTransport.Route);

    // Deterministic, and stable per task: a second launch of the same card must produce the same
    // token, or Codex's hook definition would change and demand fresh approval.
    public string Register(Guid taskId)
    {
        if (tokens.TryGetValue(taskId, out var existing))
            return existing;

        var token = $"stub-token-{++issued}";

        tokens[taskId] = token;

        return token;
    }

    public bool TryResolve(string token, out Guid taskId)
    {
        foreach (var entry in tokens)
        {
            if (entry.Value == token)
            {
                taskId = entry.Key;

                return true;
            }
        }

        taskId = Guid.Empty;

        return false;
    }

    public void Release(Guid taskId)
    {
        tokens.Remove(taskId);
        Released.Add(taskId);
    }
}
