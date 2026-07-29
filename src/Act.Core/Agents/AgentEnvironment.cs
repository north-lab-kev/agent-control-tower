namespace Act.Core.Agents;

// The environment every ACT-launched agent process gets. `ActTaskId` is load-bearing rather
// than diagnostic: it is the correlation handle for an agent that mints its own session id,
// and it lives here — on the environment — precisely so it stays out of the hook *command*.
// Codex hashes a hook's definition to decide whether it is trusted, so a command carrying
// per-session data would mint a new hash and demand fresh approval on every single launch.
public static class AgentEnvironment
{
    public const string ActTaskId = "ACT_TASK_ID";

    public const string ActHookToken = "ACT_HOOK_TOKEN";

    public const string ActHookEndpoint = "ACT_HOOK_ENDPOINT";

    public static IReadOnlyDictionary<string, string> For(
        Guid taskId,
        IDictionary<string, string> overrides,
        string? hookToken = null,
        string? hookEndpoint = null)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
        {
            if (entry.Key is string key && entry.Value is string value)
                environment[key] = value;
        }

        environment["TERM"] = "xterm-256color";
        environment[ActTaskId] = taskId.ToString("d");

        if (hookToken is not null)
            environment[ActHookToken] = hookToken;

        if (hookEndpoint is not null)
            environment[ActHookEndpoint] = hookEndpoint;

        foreach (var entry in overrides)
            environment[entry.Key] = entry.Value;

        return environment;
    }
}
