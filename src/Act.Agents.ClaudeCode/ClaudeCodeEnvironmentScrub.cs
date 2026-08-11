using Act.Core.Abstractions;

namespace Act.Agents.ClaudeCode;

// ACT started from inside a Claude Code session inherits that session's own variables, and passing
// them to the CLI ACT spawns makes it a nested session rather than a fresh one. `CLAUDECODE` is the
// signature of an enclosing session and the only way those variables reach ACT, so its absence means
// the `CLAUDE_*` here are the user's machine config and are left alone.
//
// The prefix rather than the variables that were measured: the set differs by host and by release, so
// a named list would be one CLI version from letting a new marker through in silence.
// `docs/design-notes.md` records the measurement, the cost of the prefix and what to re-test.
public sealed class ClaudeCodeEnvironmentScrub : IEnvironmentScrub
{
    public static readonly ClaudeCodeEnvironmentScrub Shared = new();

    private const string SessionMarker = "CLAUDECODE";

    private const string VariablePrefix = "CLAUDE_";

    private const string ConfigDirectory = "CLAUDE_CONFIG_DIR";

    // The keys are compared rather than looked up, because the caller owns the dictionary and nothing
    // in `IDictionary` promises it was built with a case-insensitive comparer.
    public void Apply(IDictionary<string, string> environment)
    {
        var keys = environment.Keys.ToArray();

        if (!keys.Any(IsSessionMarker))
            return;

        foreach (var key in keys.Where(FromTheEnclosingSession))
            environment.Remove(key);
    }

    private static bool IsSessionMarker(string key)
        => key.Equals(SessionMarker, StringComparison.OrdinalIgnoreCase);

    // Not the config directory: ACT reads that variable itself to find the credentials it polls usage
    // with, so a CLI that could not see it would authenticate against a different install than the
    // meter reports on.
    private static bool FromTheEnclosingSession(string key)
    {
        if (key.Equals(ConfigDirectory, StringComparison.OrdinalIgnoreCase))
            return false;

        return IsSessionMarker(key) || key.StartsWith(VariablePrefix, StringComparison.OrdinalIgnoreCase);
    }
}
