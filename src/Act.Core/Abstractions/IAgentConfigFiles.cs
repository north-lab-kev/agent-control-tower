namespace Act.Core.Abstractions;

// Generated agent config — Claude's `--settings` file, Codex's profile layer and hooks json.
// A port rather than plain `File.WriteAllText` in the adapters for two reasons: where ACT is
// allowed to put files is an infrastructure fact, and the adapter contract suite has to stay
// off the filesystem the same way `IPtyHost` keeps it off the process table.
public interface IAgentConfigFiles
{
    // ACT-owned, per task, cleared when the session ends. For anything carrying per-session data —
    // Claude's settings file holds the token.
    string Write(Guid taskId, string fileName, string content);

    // ACT-owned and shared by every task, which for Codex is not a tidiness preference but the
    // requirement: its hook definition is hashed, so a path containing a task id would mint a new
    // hash — and a new trust prompt — for every card the user ever creates.
    string WriteShared(string fileName, string content);

    // For files a CLI insists on finding in its own config home — Codex reads profile layers only
    // from `$CODEX_HOME`. ACT writes its own file there and never edits the user's.
    void WriteExternal(string absolutePath, string content);

    // The same, for a file the CLI writes back into. Codex appends its hook-trust state to the very
    // profile ACT generates, so an unconditional rewrite destroys the trust the user just granted and
    // the review screen returns on every launch. Everything from the first line beginning with
    // `tailMarker` is carried across — except a table `content` defines again, because Codex reorders
    // ACT's own tables below the marker when it saves, and a table defined twice is a profile the
    // parser rejects.
    void WriteExternalPreservingTail(string absolutePath, string content, string tailMarker);

    void DeleteExternal(string absolutePath);

    // An empty ACT-owned directory, for a query that must run *nowhere*. Both CLIs read the
    // directory they start in — CLAUDE.md, AGENTS.md, project settings, the enclosing git repo — and
    // for a question about a task's own text every one of those is context nobody asked to pay for.
    // Empty is therefore the requirement rather than a tidiness preference, which is why it is not
    // the user's working directory and not the task's.
    string ScratchDirectory();

    void Clear(Guid taskId);
}
