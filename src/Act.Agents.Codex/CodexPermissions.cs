using Act.Core.Model;

namespace Act.Agents.Codex;

// Codex spreads across two axes what Claude Code expresses in one flag: `--ask-for-approval`
// decides when it stops to ask, `--sandbox` decides what it could do if it did not. Both are
// needed to reproduce an ACT mode, which is the whole argument for `permissionMode` being a
// normalized field rather than a pass-through string.
internal sealed record CodexPermissions(string Approval, string Sandbox, bool Bypass = false)
{
    private const string Untrusted = "untrusted";

    private const string OnRequest = "on-request";

    private const string Never = "never";

    private const string ReadOnly = "read-only";

    private const string WorkspaceWrite = "workspace-write";

    public static CodexPermissions For(PermissionMode mode) => mode switch
    {
        PermissionMode.Default => new(Untrusted, WorkspaceWrite),
        PermissionMode.Plan => new(Never, ReadOnly),
        PermissionMode.AcceptEdits => new(OnRequest, WorkspaceWrite),

        PermissionMode.DontAsk => new(Never, WorkspaceWrite),
        PermissionMode.Bypass => new(Never, WorkspaceWrite, Bypass: true),

        // `Auto` lands here, and that is the point: Codex has no classifier tier, so
        // `CodexCapabilities` does not offer the mode and `Resolve` rejects it before a launch can ask
        // this map for a translation. The arm stays total rather than throwing, because a stored card
        // may still carry a mode this build no longer offers.
        _ => new(Untrusted, WorkspaceWrite),
    };
}
