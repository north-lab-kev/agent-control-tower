using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Microsoft.Extensions.Logging;

namespace Act.Agents.ClaudeCode;

// Claude Code hosted in a pseudo-terminal. ACT pre-mints the session id here, so the binding
// exists before the process does — the easier of the two acquisition modes. The opening prompt
// is a positional argument, as it is for Codex: typing it into the TUI looks equivalent but is
// a race, because a CLI that has painted its banner is not yet listening to its prompt line and
// swallows whatever arrives before it is.
public sealed class ClaudeCodeAdapter(
    IPtyHost pty,
    ICommandHost commands,
    IClock clock,
    IHookEndpoint hooks,
    IAgentConfigFiles configFiles,
    ILogger<ClaudeCodeAdapter> log) : IAgentAdapter
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

    // `-p` is the whole of what makes this cheap; the rest is turning off everything a session wants
    // and a question does not. Measured against 2.1.220: stdout is exactly the answer text, with no
    // banner and no escape codes, so there is nothing to parse.
    //
    //   * `--safe-mode` drops CLAUDE.md, skills, plugins, hooks, MCP servers, custom commands and
    //     agents — every one of which is context this question has no use for and tokens the user
    //     would pay for. `--bare` looks like the better switch and is a trap: it makes auth *strictly*
    //     `ANTHROPIC_API_KEY`/`apiKeyHelper` and never reads OAuth or the keychain, so it would fail
    //     outright for a subscription user. See `docs/agent-title-findings.md`.
    //   * `--no-session-persistence` keeps a throwaway out of the user's `/resume` picker.
    //   * The prompt goes on **stdin**, not positionally, so no quoting rule anywhere between here
    //     and the CLI can bite — see `CommandStartInfo.Input`.
    //
    // No `--permission-mode`: print mode cannot prompt, so there is nothing for one to answer.
    //
    // `--tools ""` is the single biggest saving here and is measured, not assumed: **~30,000 tokens a
    // title without it, ~5,000 with** — the tool schemas are most of what the CLI sends, and a question
    // that must not touch the disk has no use for any of them. It also turns "do not use any tools"
    // from a request in the prompt into a fact about the run. It is only usable because the prompt
    // travels on stdin: `--tools` is variadic, so a positional prompt after it would be swallowed as a
    // tool name. Keep it **last**, and keep the prompt off the command line.
    public async Task<string?> QueryAsync(
        AgentQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = Capabilities.Utility;

        List<string> arguments = ["-p", "--safe-mode", "--no-session-persistence"];

        if (model is { } utility)
        {
            arguments.Add("--model");
            arguments.Add(utility.Slug);

            if (utility.Efforts.FirstOrDefault() is { } cheapest)
            {
                arguments.Add("--effort");
                arguments.Add(cheapest);
            }
        }

        arguments.Add("--tools");
        arguments.Add(string.Empty);

        var result = await commands.RunAsync(
            new CommandStartInfo(
                request.Machine?.Binary is { Length: > 0 } binary ? binary.Trim() : DefaultBinary,
                arguments,
                configFiles.ScratchDirectory(),
                AgentEnvironment.ForQuery(request.Machine?.Env ?? new Dictionary<string, string>()),
                request.Prompt,
                request.Timeout),
            cancellationToken);

        return result.Succeeded ? result.Output : null;
    }

    // No endpoint means no hooks and a launch that still happens: ingestion is observability, and
    // losing it must never cost the user their session — which is why a settings file that cannot
    // be written is the same non-event as no endpoint at all. The file is passed by path so the
    // user's own `.claude/settings.json` is neither read nor written.
    private string? InjectHooks(Guid taskId, List<string> arguments)
    {
        if (hooks.UrlFor(Agent) is not { } url)
        {
            log.LogWarning(
                "No hook endpoint for {Agent}; task {TaskId} launches without hooks and will report "
                    + "only what its own process says.",
                Agent,
                taskId);

            return null;
        }

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
            log.LogError(
                error,
                "Could not write the {Agent} hook settings; task {TaskId} launches without hooks and "
                    + "will report only what its own process says.",
                Agent,
                taskId);

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
