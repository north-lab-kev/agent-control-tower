using Act.Core.Abstractions;

namespace Act.Core.Agents;

// The environment every ACT-launched agent process gets. `ActTaskId` is load-bearing rather
// than diagnostic: it is the correlation handle for an agent that mints its own session id,
// and it lives here — on the environment — precisely so it stays out of the hook *command*.
// Codex hashes a hook's definition to decide whether it is trusted, so a command carrying
// per-session data would mint a new hash and demand fresh approval on every single launch.
//
// The `scrub` is where an adapter subtracts what its own CLI must not inherit, and it runs first for
// that reason: ACT's variables and the install's overrides are applied over the top of it, so a user
// who sets one of the scrubbed variables deliberately still gets it. It is spelled at every call site
// rather than defaulted, because an agent with nothing to remove is a claim worth making out loud.
public static class AgentEnvironment
{
    public const string ActTaskId = "ACT_TASK_ID";

    public const string ActHookToken = "ACT_HOOK_TOKEN";

    public const string ActHookEndpoint = "ACT_HOOK_ENDPOINT";

    public static IReadOnlyDictionary<string, string> For(
        Guid taskId,
        IDictionary<string, string> overrides,
        IEnvironmentScrub? scrub,
        string? hookToken = null,
        string? hookEndpoint = null)
    {
        var environment = Inherited(scrub);

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

    // A query is not a session, and the difference is the whole of what is missing here. No task id
    // and no hook variables, because there is nothing to correlate and nothing that should report;
    // no `TERM`, because there is no terminal. The install's own overrides still apply — they are how
    // a machine says where its credentials or its proxy live. The scrub does too: a query runs the
    // same CLI, so it inherits the same thing a session would.
    public static IReadOnlyDictionary<string, string> ForQuery(
        IDictionary<string, string> overrides,
        IEnvironmentScrub? scrub)
    {
        var environment = Inherited(scrub);

        foreach (var entry in overrides)
            environment[entry.Key] = entry.Value;

        return environment;
    }

    private static Dictionary<string, string> Inherited(IEnvironmentScrub? scrub)
    {
        var environment = ProcessEnvironment();

        scrub?.Apply(environment);

        return environment;
    }

    private static Dictionary<string, string> ProcessEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
        {
            if (entry.Key is string key && entry.Value is string value)
                environment[key] = value;
        }

        return environment;
    }
}
