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

        // Codex has no classifier tier, and `on-request` — the model choosing when to ask — is
        // its nearest neighbour. It is the same pair `acceptEdits` resolves to, so the adapter
        // records an adjustment rather than pretending the two modes stayed distinct.
        PermissionMode.Auto => new(OnRequest, WorkspaceWrite),

        PermissionMode.DontAsk => new(Never, WorkspaceWrite),
        PermissionMode.Bypass => new(Never, WorkspaceWrite, Bypass: true),
        _ => new(Untrusted, WorkspaceWrite),
    };

    public static bool IsApproximate(PermissionMode mode) => mode is PermissionMode.Auto;
}
