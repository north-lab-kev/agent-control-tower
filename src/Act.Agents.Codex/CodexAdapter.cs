using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;

namespace Act.Agents.Codex;

// Codex hosted in a pseudo-terminal. Three things differ from Claude Code and all three are
// facts about the CLI rather than choices:
//
//   * there is no `--session-id`. Codex mints its own and reports it in the `SessionStart`
//     hook, so the session starts with a null `SessionId` and binds by ACT's task id until
//     that arrives. `codex resume <uuid>` takes the id afterwards.
//   * effort is not a flag. It is `-c model_reasoning_effort=<value>`, and the ladder is
//     per model.
//   * the opening prompt is a positional argument rather than something typed, which is
//     better: it is in place before the TUI paints, so there is no race with the prompt line.
public sealed class CodexAdapter(
    IPtyHost pty,
    IClock clock,
    IHookEndpoint hooks,
    IAgentConfigFiles configFiles) : IAgentAdapter
{
    public const string DefaultBinary = "codex";

    public AgentType Agent => AgentType.Codex;

    public AgentCapabilities Capabilities => CodexCapabilities.Current;

    // Codex ships a desktop app, but it has no documented deep link that adopts a CLI session
    // the way `claude://resume?session=` does, so ACT does not pretend to offer one.
    public string? DesktopHandoffUrl(string sessionId, string workingDir) => null;

    // No substitution left to make. `Auto` used to launch as `on-request` with a recorded adjustment,
    // because the form offered every mode to every agent and Codex had to do *something* with a
    // classifier tier it does not have. `CodexCapabilities` no longer offers it, so the honest outcome
    // is the resolver's rejection — and a card still carrying `Auto` from before says so at launch
    // instead of quietly running as a different mode.
    public LaunchConfigResolution Resolve(LaunchConfig config)
        => LaunchConfigResolver.Resolve(Agent, Capabilities, config);

    // Codex is the reason install discovery exists: the Store-packaged build is on no `PATH` at
    // all, so a user who has it installed still gets "not found" at spawn unless ACT goes looking.
    // Order is authority first — the CLI writes `CODEX_CLI_PATH` into its own config, which is the
    // machine's answer rather than ACT's guess — then the Store layout, then a global npm install.
    public AgentInstall Locate(IExecutableProbe probe)
    {
        if (probe.OnPath(DefaultBinary) is not null)
            return AgentInstall.OnPath;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (ConfiguredPath(probe, Path.Combine(home, ".codex", "config.toml")) is { } declared)
            return AgentInstall.At(declared);

        // The Store build lives under a build-hash directory that changes with every upgrade, so
        // the newest one is the one to take.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var storeRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");

        foreach (var build in probe.DirectoriesNewestFirst(storeRoot))
        {
            if (probe.FirstExisting([Path.Combine(build, "codex.exe")]) is { } store)
                return AgentInstall.At(store);
        }

        var npm = Environment.GetEnvironmentVariable("APPDATA");

        List<string> candidates =
        [
            Path.Combine(home, ".local", "bin", "codex.exe"),
            Path.Combine(home, ".local", "bin", "codex"),
        ];

        if (npm is { Length: > 0 })
        {
            candidates.Add(Path.Combine(npm, "npm", "codex.cmd"));
            candidates.Add(Path.Combine(npm, "npm", "codex"));
        }

        candidates.Add("/usr/local/bin/codex");

        return probe.FirstExisting(candidates) is { } found
            ? AgentInstall.At(found)
            : AgentInstall.Missing;
    }

    // `CODEX_CLI_PATH = 'C:\…\codex.exe'` — one key, quoted, in the CLI's own `config.toml`. Parsed
    // narrowly on purpose: a full TOML reader here would be a dependency and a schema to track, for
    // a line whose shape is fixed by the installer that writes it.
    private static string? ConfiguredPath(IExecutableProbe probe, string configPath)
    {
        if (probe.ReadText(configPath) is not { } config)
            return null;

        foreach (var line in config.Split('\n'))
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith("CODEX_CLI_PATH", StringComparison.Ordinal))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator < 0)
                continue;

            var value = trimmed[(separator + 1)..].Trim().Trim('\'', '"');

            if (probe.FirstExisting([value]) is { } exists)
                return exists;
        }

        return null;
    }

    public Task<IAgentSession> LaunchAsync(
        AgentLaunchRequest request,
        CancellationToken cancellationToken = default)
        => StartAsync(
            request.TaskId,
            sessionId: null,
            request.WorkingDir,
            request.Config,
            request.Size,
            leadingArguments: [],
            prompt: request.InitialPrompt,
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
            leadingArguments: ["resume", request.SessionId],
            request.Message,
            cancellationToken);

    private async Task<IAgentSession> StartAsync(
        Guid taskId,
        string? sessionId,
        string workingDir,
        LaunchConfig config,
        TerminalSize size,
        IReadOnlyList<string> leadingArguments,
        string? prompt,
        CancellationToken cancellationToken)
    {
        var resolution = Resolve(config);
        if (!resolution.CanLaunch)
            throw new InvalidOperationException(string.Join(" ", resolution.Rejections));

        var resolved = resolution.Resolved;
        var permissions = CodexPermissions.For(resolved.PermissionMode);
        var arguments = new List<string>(leadingArguments);

        if (resolved.Model is { } model)
        {
            arguments.Add("--model");
            arguments.Add(model);
        }

        if (resolved.Effort is { } effort)
        {
            arguments.Add("-c");
            arguments.Add($"model_reasoning_effort=\"{effort}\"");
        }

        if (permissions.Bypass)
        {
            arguments.Add("--dangerously-bypass-approvals-and-sandbox");
        }
        else
        {
            arguments.Add("--ask-for-approval");
            arguments.Add(permissions.Approval);
            arguments.Add("--sandbox");
            arguments.Add(permissions.Sandbox);
        }

        arguments.Add("--cd");
        arguments.Add(workingDir);

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

    // Two files: the forwarder ACT's hook definitions point at, and the profile that declares them.
    // Both are written on every launch and are byte-identical every time, which is what keeps Codex's
    // hook-trust hash stable — the token and the endpoint url ride the process environment instead.
    // ACT never passes `bypass_hook_trust`: the review screen is the user's call, and the card will
    // park on it the first time.
    //
    // Neither file is worth a session. The profile is the one path ACT writes outside its own data
    // directory, so it is also the one most likely to be refused — a read-only `~/.codex`, a
    // locked file — and a launch that died there would cost the user their task to buy ACT some
    // observability. So a failed write means no `--profile` and no token: the CLI runs, and the
    // card simply reports only what its process can say.
    private string? InjectHooks(Guid taskId, List<string> arguments)
    {
        if (hooks.UrlFor(Agent) is null)
            return null;

        try
        {
            // Shared, not per task, and that is the load-bearing part: the forwarder path appears
            // inside the hook definition Codex hashes, so a per-task path would demand fresh
            // approval for every card. One file, one hash, one review — ever.
            var forwarder = configFiles.WriteShared(
                CodexHookConfig.ForwarderFileName,
                CodexHookConfig.ComposeForwarder());

            // The tail is carried across because Codex appends `[hooks.state]` — the trust hashes —
            // to this very file, and rewriting it wholesale throws away the review the user just
            // answered, bringing the nine-hook gate back on every launch. Comparing the file against
            // ACT's own bytes is not enough to detect that: Codex rewrites the whole thing in its own
            // formatting when it saves, so what ACT wrote does not come back byte-identical.
            configFiles.WriteExternalPreservingTail(
                CodexHookConfig.ProfilePath(CodexHookConfig.ResolveCodexHome()),
                CodexHookConfig.ComposeProfile(forwarder),
                CodexHookConfig.TrustStateKey);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        arguments.Add("--profile");
        arguments.Add(CodexHookConfig.ProfileName);

        return hooks.Register(taskId);
    }
}
