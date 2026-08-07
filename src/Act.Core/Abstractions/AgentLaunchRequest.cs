using Act.Core.Model;

namespace Act.Core.Abstractions;

// `SessionId` is pre-minted by ACT so the binding exists before the process does.
// `InitialPrompt` is the user's task text and nothing else — ACT injects no instruction
// block of its own, so the opening prompt carries no ACT-authored preamble.
// `Size` travels with the request because a pseudo-terminal is sized at spawn.
// `Attachments` is the user's own files, which each adapter delivers its own way — see
// `AttachmentInstruction`.
public sealed record AgentLaunchRequest(
    Guid TaskId,
    string SessionId,
    string WorkingDir,
    string InitialPrompt,
    LaunchConfig Config,
    TerminalSize Size,
    AgentAttachments? Attachments = null);
