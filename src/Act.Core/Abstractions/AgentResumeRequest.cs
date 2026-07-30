using Act.Core.Model;

namespace Act.Core.Abstractions;

// Reopening a completed card, retrying an error, and re-attaching after ACT restarted all
// resume the same session id into a fresh terminal. `Message` is null for a bare resume,
// which drops the user at the prompt; when set, it is the opening prompt of the resumed session.
// `InitialPrompt` travels along for the fresh-seed fallback when the transcript is gone.
public sealed record AgentResumeRequest(
    Guid TaskId,
    string SessionId,
    string WorkingDir,
    string Preamble,
    string InitialPrompt,
    string? Message,
    LaunchConfig Config,
    TerminalSize Size);
