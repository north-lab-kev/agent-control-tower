using Act.Core.Abstractions;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

// A pinned snapshot of `claude-code` 2.1.220, and a thing to re-check on a version bump. The CLI
// publishes no machine-readable list and scraping its help text would fail back to this one
// silently — `docs/design-notes.md` records what was checked and why nothing better exists. Every
// listed model takes the same effort ladder, unlike Codex's per-model one.
public static class ClaudeCodeCapabilities
{
    private static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh", "max"];

    // Context windows are read out of the CLI's own model table rather than guessed. A model this
    // list does not know shows no percentage at all, which is the intended failure — the
    // alternative is a bar measured against the wrong window.
    private const int Million = 1_000_000;

    // All six, and each maps to a distinct `--permission-mode` value the CLI names itself, so there is
    // nothing approximate here — unlike Codex, which offers five.
    private static readonly IReadOnlyList<PermissionMode> Modes =
    [
        PermissionMode.Default,
        PermissionMode.Plan,
        PermissionMode.AcceptEdits,
        PermissionMode.Auto,
        PermissionMode.DontAsk,
        PermissionMode.Bypass,
    ];

    public static AgentCapabilities Current { get; } = new(
        [
            new AgentModel("opus", "Opus 5", Efforts, "high", Million),
            new AgentModel("sonnet", "Sonnet 5", Efforts, "medium", Million),
            new AgentModel("fable", "Fable 5", Efforts, "medium", Million),
            new AgentModel("haiku", "Haiku 4.5", Efforts, "low", 200_000),
        ],
        "sonnet",
        Modes,
        DesktopHandoff: true,

        // The cheapest of the four, and the only one whose context window is not a million tokens —
        // which is exactly right for the questions ACT asks itself, none of which need one.
        UtilityModel: "haiku");
}
