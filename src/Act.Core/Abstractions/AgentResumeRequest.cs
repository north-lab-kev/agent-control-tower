using Act.Core.Model;

namespace Act.Core.Abstractions;

// Send-back, answering a question and retrying an error all resume the same session id.
// `Message` is null for a bare retry, which continues where the session failed.
// `InitialPrompt` travels along for the fresh-seed fallback when the transcript is gone.
public sealed record AgentResumeRequest(
    Guid TaskId,
    string SessionId,
    string WorkingDir,
    string Preamble,
    string InitialPrompt,
    string? Message,
    LaunchConfig Config);
