using Act.Core.Model;

namespace Act.Core.Abstractions;

// `SessionId` is pre-minted by ACT so the binding exists before the process does.
// `Preamble` stays separate from `InitialPrompt` because how it reaches the agent is the
// adapter's call — prepended to the prompt, a system-prompt flag, a settings file.
public sealed record AgentLaunchRequest(
    Guid TaskId,
    string SessionId,
    string WorkingDir,
    string Preamble,
    string InitialPrompt,
    LaunchConfig Config);
