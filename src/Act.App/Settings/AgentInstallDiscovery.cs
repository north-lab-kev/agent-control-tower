using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.App.Settings;

// Finds each agent's CLI at startup so the common case needs no configuration at all. Three
// outcomes, and each does something different:
//
//   * **On `PATH`** — change nothing. An empty setting *means* "resolve by name", and that keeps
//     working when the install upgrades itself out from under a recorded path.
//   * **Off `PATH` but installed** — record the path, because a pty spawns with an explicit image
//     and would otherwise fail with "not found" for a CLI that is sitting right there.
//   * **Not installed** — leave the path empty and, on the **first** pass only, switch the agent
//     off in the task form. A machine with one agent is a normal machine, and offering the other
//     one can only produce cards that die at spawn.
//
// A path the user typed themselves is never touched, whatever the probe finds: it is the one
// answer that came from a human, and someone who points ACT at a specific build means it.
public sealed class AgentInstallDiscovery(
    IEnumerable<IAgentAdapter> adapters,
    IExecutableProbe probe,
    UserSettingsService settings,
    ILogger<AgentInstallDiscovery> log)
{
    public void Run()
    {
        var first = !settings.AgentInstallsProbed;

        foreach (var adapter in adapters)
        {
            var current = settings.Defaults(adapter.Agent);
            var install = Locate(adapter);

            if (install.ExplicitPath is { } path && string.IsNullOrWhiteSpace(current.Binary))
                current.Binary = path;

            // Only ever on the first pass: after that the switch belongs to the user, so
            // re-enabling an agent ACT could not find has to stick.
            if (first && !install.Found)
                current.Enabled = false;

            settings.SetDefaults(current);

            log.LogInformation(
                "{Agent}: {Outcome}.",
                adapter.Agent,
                Describe(install, current));
        }

        if (first)
            settings.MarkAgentInstallsProbed();
    }

    // A discovery that throws must not stop the app: it runs before the board is usable, and the
    // worst case without it is the configuration the user had yesterday.
    private AgentInstall Locate(IAgentAdapter adapter)
    {
        try
        {
            return adapter.Locate(probe);
        }
        catch (Exception error)
        {
            log.LogWarning(error, "Locating {Agent} failed; leaving its settings alone.", adapter.Agent);

            return AgentInstall.OnPath;
        }
    }

    private static string Describe(AgentInstall install, AgentDefaults current) => install switch
    {
        { ExplicitPath: { } path } => $"found at {path}",
        { Found: true } => "found on PATH",
        _ when current.Enabled => "not found (left enabled by choice)",
        _ => "not found, disabled in the task form",
    };
}
