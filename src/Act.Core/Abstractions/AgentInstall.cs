namespace Act.Core.Abstractions;

// What startup discovery found for one agent. Three outcomes, and they are genuinely different:
//
//   * `OnPath`      — installed and resolvable by name. ACT stores **nothing**, because an empty
//                     setting means "find it on PATH" and that keeps working when the install moves.
//   * `At(path)`    — installed somewhere off `PATH`, so the path has to be recorded or every
//                     launch fails at spawn. The Store-packaged Codex is the case that forced this.
//   * `Missing`     — not installed. Not an error: a machine with one agent is a normal machine.
public sealed record AgentInstall(bool Found, string? ExplicitPath)
{
    public static AgentInstall OnPath { get; } = new(true, null);

    public static AgentInstall Missing { get; } = new(false, null);

    public static AgentInstall At(string path) => new(true, path);

    // The install layouts every CLI shares when it is not on `PATH`: its installer's own
    // `~/.local/bin`, a global npm install, and `/usr/local/bin`. One list for every adapter, so a
    // new layout — or a missing `.cmd` variant — cannot be learned by one agent and not the other;
    // anything agent-specific (the Codex Store build, `CODEX_CLI_PATH`) stays in that adapter.
    public static IReadOnlyList<string> WellKnownPaths(string binary)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var npm = Environment.GetEnvironmentVariable("APPDATA");

        List<string> candidates =
        [
            Path.Combine(home, ".local", "bin", $"{binary}.exe"),
            Path.Combine(home, ".local", "bin", binary),
        ];

        if (npm is { Length: > 0 })
        {
            candidates.Add(Path.Combine(npm, "npm", $"{binary}.cmd"));
            candidates.Add(Path.Combine(npm, "npm", binary));
        }

        candidates.Add($"/usr/local/bin/{binary}");

        return candidates;
    }
}
