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

    void DeleteExternal(string absolutePath);

    void Clear(Guid taskId);
}
