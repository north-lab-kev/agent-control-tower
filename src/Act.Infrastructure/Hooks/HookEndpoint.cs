using System.Collections.Concurrent;
using System.Security.Cryptography;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.Infrastructure.Hooks;

public sealed class HookEndpoint : IHookEndpoint
{
    public const string TokenHeader = HookTransport.TokenHeader;

    public const string RoutePrefix = HookTransport.RoutePrefix;

    private readonly ConcurrentDictionary<string, Guid> byToken = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<Guid, string> byTask = new();

    public Uri? BaseAddress { get; private set; }

    public int Port { get; private set; }

    public void Bind(int port)
    {
        Port = port;
        BaseAddress = new Uri($"http://127.0.0.1:{port}");
    }

    public static string RouteFor(AgentType agent) => agent switch
    {
        AgentType.ClaudeCode => HookTransport.ClaudeRoute,
        AgentType.Codex => HookTransport.CodexRoute,
        _ => $"{RoutePrefix}/unknown",
    };

    public Uri? UrlFor(AgentType agent)
        => BaseAddress is { } address ? new Uri(address, RouteFor(agent)) : null;

    // Re-registering a task keeps its existing token: a resume reuses the card's session, and a
    // new token would be a new hook definition for Codex and a fresh trust prompt with it.
    public string Register(Guid taskId)
    {
        if (byTask.TryGetValue(taskId, out var existing))
            return existing;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        byTask[taskId] = token;
        byToken[token] = taskId;

        return token;
    }

    public bool TryResolve(string token, out Guid taskId)
    {
        taskId = Guid.Empty;

        return !string.IsNullOrEmpty(token) && byToken.TryGetValue(token, out taskId);
    }

    public void Release(Guid taskId)
    {
        if (byTask.TryRemove(taskId, out var token))
            byToken.TryRemove(token, out _);
    }
}
