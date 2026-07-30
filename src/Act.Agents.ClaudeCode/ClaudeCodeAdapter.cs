using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

// Claude Code hosted in a pseudo-terminal. ACT pre-mints the session id here, so the binding
// exists before the process does — the easier of the two acquisition modes. The opening prompt
// is a positional argument, as it is for Codex: typing it into the TUI looks equivalent but is
// a race, because a CLI that has painted its banner is not yet listening to its prompt line and
// swallows whatever arrives before it is.
public sealed class ClaudeCodeAdapter(IPtyHost pty, IClock clock) : IAgentAdapter
{
    public const string DefaultBinary = "claude";

    private static readonly TerminalSubmitProfile Submit = TerminalSubmitProfile.Default;

    public AgentType Agent => AgentType.ClaudeCode;

    public AgentCapabilities Capabilities => ClaudeCodeCapabilities.Current;

    public LaunchConfigResolution Resolve(LaunchConfig config)
        => LaunchConfigResolver.Resolve(Agent, Capabilities, config);

    // Confirmed against the desktop app's own bundle: the parameter is `session`, not
    // `sessionId`, and the route locates the transcript itself, so no working directory.
    public string? DesktopHandoffUrl(string sessionId, string workingDir)
        => $"claude://resume?session={Uri.EscapeDataString(sessionId)}";

    public Task<IAgentSession> LaunchAsync(
        AgentLaunchRequest request,
        CancellationToken cancellationToken = default)
        => StartAsync(
            request.TaskId,
            request.SessionId,
            request.WorkingDir,
            request.Config,
            request.Size,
            ["--session-id", request.SessionId],
            $"{request.Preamble}\n\n{request.InitialPrompt}",
            cancellationToken);

    public Task<IAgentSession> ResumeAsync(
        AgentResumeRequest request,
        CancellationToken cancellationToken = default)
        => StartAsync(
            request.TaskId,
            request.SessionId,
            request.WorkingDir,
            request.Config,
            request.Size,
            ["--resume", request.SessionId],
            request.Message,
            cancellationToken);

    private async Task<IAgentSession> StartAsync(
        Guid taskId,
        string sessionId,
        string workingDir,
        LaunchConfig config,
        TerminalSize size,
        IReadOnlyList<string> sessionArguments,
        string? prompt,
        CancellationToken cancellationToken)
    {
        var resolution = Resolve(config);
        if (!resolution.CanLaunch)
            throw new InvalidOperationException(string.Join(" ", resolution.Rejections));

        var resolved = resolution.Resolved;
        var arguments = new List<string>(sessionArguments);

        if (resolved.Model is { } model)
        {
            arguments.Add("--model");
            arguments.Add(model);
        }

        if (resolved.Effort is { } effort)
        {
            arguments.Add("--effort");
            arguments.Add(effort);
        }

        arguments.Add("--permission-mode");
        arguments.Add(PermissionModeFlag(resolved.PermissionMode));

        foreach (var tool in resolved.AllowedTools)
        {
            arguments.Add("--allowed-tools");
            arguments.Add(tool);
        }

        foreach (var tool in resolved.DisallowedTools)
        {
            arguments.Add("--disallowed-tools");
            arguments.Add(tool);
        }

        arguments.AddRange(resolved.ExtraFlags);

        // Positional, and last: everything after it would be read as part of the prompt.
        if (!string.IsNullOrWhiteSpace(prompt))
            arguments.Add(prompt);

        var process = await pty.StartAsync(
            new PtyStartInfo(
                resolved.AgentBinary ?? DefaultBinary,
                arguments,
                workingDir,
                AgentEnvironment.For(taskId, resolved.Env),
                size),
            cancellationToken);

        return new PtyAgentSession(taskId, sessionId, process, Submit, clock);
    }

    private static string PermissionModeFlag(PermissionMode mode) => mode switch
    {
        PermissionMode.Default => "default",
        PermissionMode.Plan => "plan",
        PermissionMode.AcceptEdits => "acceptEdits",
        PermissionMode.Auto => "auto",
        PermissionMode.DontAsk => "dontAsk",
        PermissionMode.Bypass => "bypassPermissions",
        _ => "default",
    };
}
