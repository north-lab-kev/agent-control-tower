using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.Agents.ClaudeCode;

// Claude Code hosted in a pseudo-terminal. ACT pre-mints the session id here, so the binding
// exists before the process does — the easier of the two acquisition modes. The opening prompt
// is a positional argument, as it is for Codex: typing it into the TUI looks equivalent but is
// a race, because a CLI that has painted its banner is not yet listening to its prompt line and
// swallows whatever arrives before it is.
public sealed class ClaudeCodeAdapter(
    IPtyHost pty,
    IClock clock,
    IHookEndpoint hooks,
    IAgentConfigFiles configFiles) : IAgentAdapter
{
    public const string DefaultBinary = "claude";

    public AgentType Agent => AgentType.ClaudeCode;

    public AgentCapabilities Capabilities => ClaudeCodeCapabilities.Current;

    public LaunchConfigResolution Resolve(LaunchConfig config)
        => LaunchConfigResolver.Resolve(Agent, Capabilities, config);

    // `PATH` first, because that is the install the CLI's own installer produces and the one that
    // survives an upgrade. The fallbacks are the two shapes that are not on `PATH` on a fresh
    // machine: the installer's own `~/.local/bin`, and a global npm install.
    public AgentInstall Locate(IExecutableProbe probe)
    {
        if (probe.OnPath(DefaultBinary) is not null)
            return AgentInstall.OnPath;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var npm = Environment.GetEnvironmentVariable("APPDATA");

        List<string> candidates =
        [
            Path.Combine(home, ".local", "bin", "claude.exe"),
            Path.Combine(home, ".local", "bin", "claude"),
        ];

        if (npm is { Length: > 0 })
        {
            candidates.Add(Path.Combine(npm, "npm", "claude.cmd"));
            candidates.Add(Path.Combine(npm, "npm", "claude"));
        }

        candidates.Add("/usr/local/bin/claude");

        return probe.FirstExisting(candidates) is { } found
            ? AgentInstall.At(found)
            : AgentInstall.Missing;
    }

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
            request.InitialPrompt,
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

        var hookToken = InjectHooks(taskId, arguments);

        arguments.AddRange(resolved.ExtraFlags);

        // Positional, and last: everything after it would be read as part of the prompt.
        if (!string.IsNullOrWhiteSpace(prompt))
            arguments.Add(prompt);

        var process = await pty.StartAsync(
            new PtyStartInfo(
                resolved.AgentBinary ?? DefaultBinary,
                arguments,
                workingDir,
                AgentEnvironment.For(
                    taskId,
                    resolved.Env,
                    hookToken,
                    hooks.UrlFor(Agent)?.ToString()),
                size),
            cancellationToken);

        return new PtyAgentSession(taskId, sessionId, process, clock);
    }

    // No endpoint means no hooks and a launch that still happens: ingestion is observability, and
    // losing it must never cost the user their session — which is why a settings file that cannot
    // be written is the same non-event as no endpoint at all. The file is passed by path so the
    // user's own `.claude/settings.json` is neither read nor written.
    private string? InjectHooks(Guid taskId, List<string> arguments)
    {
        if (hooks.UrlFor(Agent) is not { } url)
            return null;

        var token = hooks.Register(taskId);

        string path;

        try
        {
            path = configFiles.Write(
                taskId,
                ClaudeCodeHookSettings.FileName,
                ClaudeCodeHookSettings.Compose(url, token));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        arguments.Add("--settings");
        arguments.Add(path);

        return token;
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
