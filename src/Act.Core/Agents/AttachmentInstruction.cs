using Act.Core.Resources;

namespace Act.Core.Agents;

// How a file reaches an agent: as an absolute path in the opening prompt, never as content.
// The prompt is a positional command-line argument for both CLIs, and Windows caps a command
// line at ~32,767 characters — so inlining even one modest log would spend the whole budget and
// a large one would fail the spawn outright. A path costs a line and the agent reads the file
// with its own tools, on the machine it is already running on.
//
// Appended at launch and never stored, exactly like `AutoGitInstruction`: `Card.InitialPrompt`
// stays verbatim for the life of the task, which is what the form promises.
//
// Called by the *adapter* rather than by the launcher, because which files a CLI can take
// natively is a fact about that CLI — Codex lifts images onto the first turn with `-i` and only
// the rest need naming here, while Claude Code has no such flag and needs all of them.
public static class AttachmentInstruction
{
    public static string Append(string prompt, IReadOnlyList<string> paths)
        => paths.Count == 0
            ? prompt
            : $"{prompt}\n\n{CoreStrings.Attachments_Header}\n{string.Join("\n", paths)}";
}
