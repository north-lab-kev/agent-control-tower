using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

// Hard-coded because the CLI offers nothing better. Checked against `claude --help` on
// 2.1.220: there is no models subcommand — an unrecognised argument is read as the *prompt*,
// so `claude models` starts a session and answers the question — and `--model` documents only
// that it takes an alias, naming `fable`, `opus` and `sonnet` as examples. The one list the
// CLI does enumerate is `--effort (low, medium, high, xhigh, max)`; it is not worth scraping
// help text for, because a wording change would fail back to this list silently and leave the
// staleness it was meant to fix. So: a pinned fact about a CLI version, and a thing to
// re-check on a bump. Every listed model takes the same effort ladder, unlike Codex's
// per-model one.
public static class ClaudeCodeCapabilities
{
    private static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh", "max"];

    public static AgentCapabilities Current { get; } = new(
        [
            new AgentModel("opus", "Opus 5", Efforts, "high"),
            new AgentModel("sonnet", "Sonnet 5", Efforts, "medium"),
            new AgentModel("fable", "Fable 5", Efforts, "medium"),
            new AgentModel("haiku", "Haiku 4.5", Efforts, "low"),
        ],
        "sonnet",
        new HashSet<PermissionMode>(Enum.GetValues<PermissionMode>()),
        DesktopHandoff: true);
}
