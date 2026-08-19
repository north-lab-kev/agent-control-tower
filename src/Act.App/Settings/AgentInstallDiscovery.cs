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
// A path that **still exists** is never touched, whatever the probe finds: it is the answer that came
// from a human, and someone who points ACT at a specific build means it.
//
// A path that has **stopped existing** is replaced, and that is not the same rule bending. Codex
// installs under a build-hash directory that changes on every upgrade, so the path recorded here — by
// ACT itself, in the empty case above — dies the next time the CLI updates itself, and nothing tells
// anyone: an empty-looking setting was never empty, so the guard above skipped it, and every launch
// failed at spawn with "not found" until the path was re-pasted by hand. A dead path is not a
// configuration worth protecting. Replacing it is logged, because overwriting something a human may
// have typed is not a thing to do quietly.
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

            if (install.ExplicitPath is { } path && Records(current.Binary, path))
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

    // Whether what discovery found should be written over what the setting holds. Three cases, and
    // the middle one is the whole point of the method existing:
    //
    //   * **empty** — record it, so the common off-`PATH` install needs no configuration.
    //   * **a bare name** — never. No separator means "resolve this on `PATH`", which is a rule the
    //     launch follows too (see `AgentBinaryCheck`), and a name that stops resolving is a broken
    //     install rather than a moved one.
    //   * **a path** — only when it no longer exists. A live path is somebody's decision; a dead one
    //     is a launch that fails at spawn for a CLI sitting right there under a new build hash.
    private bool Records(string? binary, string discovered)
    {
        if (string.IsNullOrWhiteSpace(binary))
            return true;

        var current = binary.Trim();

        if (!current.Contains(Path.DirectorySeparatorChar) && !current.Contains(Path.AltDirectorySeparatorChar))
            return false;

        if (probe.FirstExisting([current]) is not null)
            return false;

        log.LogWarning(
            "The recorded executable {Missing} no longer exists; replacing it with {Discovered}, which "
                + "discovery just found. A CLI that installs under a versioned directory does this on "
                + "every upgrade.",
            current,
            discovered);

        return true;
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
