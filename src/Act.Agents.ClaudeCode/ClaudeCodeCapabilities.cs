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

    // The windows are **read out of the CLI's own model table** (`claude.exe`, 2.1.220): each model
    // entry carries `context: { window }`, and it says 1e6 for `claude-opus-5`, `claude-sonnet-5` and
    // `claude-fable-5`, 200000 for `claude-haiku-4-5`. So this is a measured fact rather than a
    // guess, and the same thing to re-check on a bump as the model list itself. A model this list
    // does not know shows no percentage at all, which is the intended failure — the alternative is a
    // bar measured against the wrong window.
    //
    // Two ways the CLI's *effective* window ends up smaller than the model's, neither of which ACT
    // can observe: `CLAUDE_CODE_MAX_CONTEXT_TOKENS` overrides it, and a session whose 1M credits are
    // blocked falls back to 200k. Both make ACT's percentage read low, never high.
    private const int Million = 1_000_000;

    public static AgentCapabilities Current { get; } = new(
        [
            new AgentModel("opus", "Opus 5", Efforts, "high", Million),
            new AgentModel("sonnet", "Sonnet 5", Efforts, "medium", Million),
            new AgentModel("fable", "Fable 5", Efforts, "medium", Million),
            new AgentModel("haiku", "Haiku 4.5", Efforts, "low", 200_000),
        ],
        "sonnet",
        new HashSet<PermissionMode>(Enum.GetValues<PermissionMode>()),
        DesktopHandoff: true);
}
