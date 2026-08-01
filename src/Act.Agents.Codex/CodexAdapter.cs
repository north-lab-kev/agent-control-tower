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

    private static readonly TerminalSubmitProfile Submit = TerminalSubmitProfile.Default;

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

        return new PtyAgentSession(taskId, sessionId, process, Submit, clock);
    }

    // Two files: the forwarder ACT's hook definitions point at, and the profile that declares them.
    // Both are written on every launch and are byte-identical every time, which is what keeps Codex's
    // hook-trust hash stable — the token and the endpoint url ride the process environment instead.
    // ACT never passes `bypass_hook_trust`: the review screen is the user's call, and the card will
    // park on it the first time.
    //
    // There was a third file, an `act-hooks.json` in ACT's own directory, from when the profile shape
    // was thought to be a path to one. Codex only auto-discovers that json at `$CODEX_HOME/hooks.json`
    // — the user's file, which ACT will not write — so ACT's copy was read by nobody. Deleted
    // 2026-07-31 with the rest of the blind-era scaffolding.
    private string? InjectHooks(Guid taskId, List<string> arguments)
    {
        if (hooks.UrlFor(Agent) is null)
            return null;

        var token = hooks.Register(taskId);

        // Shared, not per task, and that is the load-bearing part: the forwarder path appears inside
        // the hook definition Codex hashes, so a per-task path would demand fresh approval for
        // every card. One file, one hash, one review — ever.
        var forwarder = configFiles.WriteShared(
            CodexHookConfig.ForwarderFileName,
            CodexHookConfig.ComposeForwarder());

        // The profile declares the handlers as `[[hooks.<Event>]]` tables. It used to write
        // `hooks = "<path>"`, which this CLI rejects outright — *"invalid type: string … expected
        // struct HooksToml"* — so every Codex launch died on exit code 1 with an empty terminal
        // before the shape was re-measured. `CodexHookConfig.ComposeProfile` records how.
        // Carrying the tail across, because Codex appends `[hooks.state]` — the trust hashes — to this
        // very file. Rewriting it wholesale threw away the review the user had just answered, so the
        // nine-hook gate came back on every launch. Comparing the file with ACT's own bytes is not
        // enough: Codex rewrites the whole thing in its own formatting when it saves, so the block ACT
        // wrote does not come back byte-identical. Measured 2026-07-31.
        configFiles.WriteExternalPreservingTail(
            CodexHookConfig.ProfilePath(CodexHookConfig.ResolveCodexHome()),
            CodexHookConfig.ComposeProfile(forwarder),
            CodexHookConfig.TrustStateKey);

        arguments.Add("--profile");
        arguments.Add(CodexHookConfig.ProfileName);

        return token;
    }
}
