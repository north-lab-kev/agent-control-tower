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
}
