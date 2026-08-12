using Act.Core.Model;

namespace Act.Core.Agents;

// The config an adapter is actually handed: what the *task* asked for, plus what this *machine*
// always adds. The split is the point — a card owns the model, the reasoning effort and the
// permission mode, because those describe the work; the settings own the binary, the flags and the
// environment, because those describe the install.
//
// The machine half always wins, and is applied even when there is nothing to apply. A card written
// before the settings existed still carries its own copy of these three, and reading it back would
// resurrect a path the user has since corrected in one place — so the composition clears them
// first and there is no route by which a card's stale copy reaches a CLI.
public static class LaunchComposition
{
    public static LaunchConfig Compose(LaunchConfig task, AgentDefaults? machine)
    {
        var composed = task.Copy();

        composed.AgentBinary = Trimmed(machine?.Binary);
        composed.ExtraFlags = machine is null ? [] : [.. machine.ExtraFlags];
        composed.Env = machine is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(machine.Env);

        return composed;
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
