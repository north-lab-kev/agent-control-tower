using System.Text.Json;
using Act.Agents.Codex;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// These pin what ACT *writes*, which is where the bug that kept every Codex hook from ever running
// lived — a quoted program token. Ingestion through them was verified live on 2026-07-31; see
// `docs/codex-hooks-findings.md`.
public class CodexHookInjectionTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    [Fact]
    public async Task A_launch_layers_acts_own_profile()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, new StubHookEndpoint(), new StubAgentConfigFiles());

        pty.Last.Arguments.Should().ContainInOrder("--profile", CodexHookConfig.ProfileName);
    }

    // The regression this pins cost a whole verification run: the profile used to carry
    // `hooks = "<path>"`, which the CLI rejects outright — *"invalid type: string … expected struct
    // HooksToml"* — so every Codex card died on exit code 1 with an empty terminal. The shape below
    // was measured by type-probing the parser (see `CodexHookConfig.ComposeProfile`), and the parser
    // **ignores unknown keys**, so nothing but an assertion on the exact shape can catch a drift.
    [Fact]
    public async Task The_profile_declares_handlers_as_event_tables_not_a_file_path()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var profile = files.External.Should().ContainSingle().Which;

        profile.Key.Should().EndWith($"{CodexHookConfig.ProfileName}.config.toml");
        profile.Value.Should().StartWith("#");
        profile.Value.Should().NotContain("hooks = \"");
        profile.Value.Should().Contain("[[hooks.SessionStart]]")
            .And.Contain("[[hooks.SessionStart.hooks]]")
            .And.Contain("matcher = ")
            .And.Contain("type = \"command\"");
    }

    // Every event ACT observes has to be declared, or the ones left out are simply never reported.
    // Spelled out here rather than compared against another generated file: this list is the actual
    // requirement, and asserting it against a second thing ACT writes only proves the two agree.
    [Theory]
    [InlineData("SessionStart")]
    [InlineData("SessionEnd")]
    [InlineData("UserPromptSubmit")]
    [InlineData("PreToolUse")]
    [InlineData("PermissionRequest")]
    [InlineData("PostToolUse")]
    [InlineData("PreCompact")]
    [InlineData("PostCompact")]
    [InlineData("Stop")]
    public async Task The_profile_declares_every_event_act_observes(string name)
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var profile = files.External.Single().Value;

        profile.Should().Contain($"[[hooks.{name}]]")
            .And.Contain($"[[hooks.{name}.hooks]]");

        CommandFor(profile, name).Should().EndWith($" {name}");
    }

    // The command value wraps the forwarder path in quotes so a path with spaces survives, and TOML
    // basic strings need both those quotes and the separators escaped. Leaving the quotes bare
    // produced a file the parser could not read — caught only by handing the real CLI the real bytes.
    [Fact]
    public async Task The_profile_escapes_the_quotes_and_separators_in_the_command()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var profile = files.External.Single().Value;
        var value = profile.Split('\n')
            .First(line => line.StartsWith("command = ", StringComparison.Ordinal))
            .Trim();

        value.Should().StartWith("command = \"").And.EndWith("\\\" SessionStart\"");
        value.Should().Contain("\\\"");
        value.Should().NotContain(@"\\\\\");
    }

    // The regression that made every Codex hook fail from the very first launch, and the whole reason
    // no payload had ever arrived: **Codex takes quotes literally when it resolves the program.** A
    // quoted first token is a program named `"C:\…"`, which does not exist — `hook exited with code
    // 1`, script never reached. Measured 2026-07-31 across five candidate shapes; an unquoted program
    // runs, and a quoted argument after it is fine because `cmd` parses that part.
    [Fact]
    public async Task The_commands_program_is_never_quoted_because_codex_reads_the_quotes_literally()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var command = CommandFor(files.External.Single().Value, "SessionStart");

        command.Should().NotStartWith("\"");

        if (OperatingSystem.IsWindows())
            command.Should().Contain("cmd.exe /c \"");
        else
            command.Should().StartWith("/bin/sh \"");

        command.Should().EndWith(" SessionStart");
        command.Should().Contain(CodexHookConfig.ForwarderFileName);
    }

    // The forwarder lives under `%LOCALAPPDATA%`, so a username with a space puts a space in the path.
    // Quoting it is safe *only* after an unquoted program token, which is what makes both halves of
    // the rule above load-bearing at once.
    [Fact]
    public async Task A_forwarder_path_with_a_space_stays_quoted_after_the_unquoted_program()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var command = CommandFor(files.External.Single().Value, "Stop");

        var quoted = command.IndexOf('"');

        quoted.Should().BeGreaterThan(0);
        command[..quoted].Should().NotContain("\"");
        command.Should().Contain($"\"{Path.Combine(Path.GetTempPath(), "act-tests", CodexHookConfig.ForwarderFileName)}\"");
    }

    // Codex appends its trust hashes to ACT's own profile, so a relaunch has to carry them across.
    // Rewriting the file threw the review away and the nine-hook gate came back on every launch — and
    // comparing bytes is not enough, because Codex reformats ACT's block when it saves, which is
    // exactly how the first attempt at this failed live.
    [Fact]
    public async Task A_relaunch_carries_codexs_trust_state_across_even_when_it_reformatted_the_file()
    {
        var files = new StubAgentConfigFiles();
        var endpoint = new StubHookEndpoint();

        await using (var one = await LaunchAsync(new StubPtyHost(), endpoint, files))
        {
        }

        var path = files.External.Single().Key;

        const string state = """
            [hooks.state]

            [hooks.state.'session_start:0:0']
            trusted_hash = "sha256:deadbeef"
            """;

        files.External[path] = files.External[path].ReplaceLineEndings("\n") + "\n" + state;

        await using (var two = await LaunchAsync(new StubPtyHost(), endpoint, files, Guid.NewGuid()))
        {
        }

        files.External[path].Should().Contain("trusted_hash = \"sha256:deadbeef\"");
        files.External[path].Should().Contain("[[hooks.SessionStart]]");
        files.Preserved.Should().Contain(path);
    }

    // Only `command` handlers, because they are the only type Codex actually runs — `prompt` and
    // `agent` parse and are skipped. `PermissionRequest` is the one Codex has and Claude Code does not:
    // an explicit event for a waiting prompt rather than a notification message to classify.
    [Fact]
    public async Task Every_handler_is_a_command_handler()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var profile = files.External.Single().Value;

        profile.Should().Contain("type = \"command\"");
        profile.Should().NotContain("type = \"prompt\"").And.NotContain("type = \"agent\"");
        CommandFor(profile, "PermissionRequest").Should().EndWith(" PermissionRequest");
    }

    // The load-bearing property of the whole design: Codex hashes each handler definition and
    // re-prompts for trust when it changes. Two launches of the same card — and of a different one
    // — must produce byte-identical declarations, which is only possible while the token and the
    // endpoint url stay in the environment.
    [Fact]
    public async Task The_hook_definition_is_byte_identical_across_launches_and_carries_no_secret()
    {
        var endpoint = new StubHookEndpoint();
        var first = new StubAgentConfigFiles();
        var second = new StubAgentConfigFiles();

        await using (var one = await LaunchAsync(new StubPtyHost(), endpoint, first))
        {
        }

        await using (var two = await LaunchAsync(new StubPtyHost(), endpoint, second, Guid.NewGuid()))
        {
        }

        var a = first.External.Single().Value;
        var b = second.External.Single().Value;

        a.Should().Be(b);
        a.Should().NotContain(endpoint.Register(TaskId));
        a.Should().NotContain("127.0.0.1");
    }

    [Fact]
    public async Task The_forwarder_reads_the_endpoint_and_token_from_the_environment()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var forwarder = files.Content(CodexHookConfig.ForwarderFileName)!;

        forwarder.Should().Contain(AgentEnvironment.ActHookEndpoint);
        forwarder.Should().Contain(AgentEnvironment.ActHookToken);
        forwarder.Should().Contain(HookTransport.TokenHeader);
    }

    // Observability only: a hook that can fail is a hook that can wedge the user's session.
    [Fact]
    public async Task The_forwarder_always_succeeds()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var forwarder = files.Content(CodexHookConfig.ForwarderFileName)!;

        forwarder.Should().Contain(OperatingSystem.IsWindows() ? "exit /b 0" : "exit 0");
        forwarder.Should().NotContain("permissionDecision");
        forwarder.Should().NotContain("\"block\"");
    }

    // The review screen is the user's call. ACT opting them out of a security gate would be ACT
    // deciding something, which is the one thing it does not do.
    [Fact]
    public async Task Act_never_bypasses_hook_trust()
    {
        var pty = new StubPtyHost();

        await using var session = await LaunchAsync(pty, new StubHookEndpoint(), new StubAgentConfigFiles());

        pty.Last.Arguments.Should().NotContain(argument => argument.Contains("bypass_hook_trust"));
        pty.Last.Arguments.Should().NotContain("--dangerously-bypass-hook-trust");
    }

    [Fact]
    public async Task With_no_endpoint_the_launch_still_happens_without_hooks()
    {
        var pty = new StubPtyHost();
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(pty, StubHookEndpoint.Offline(), files);

        pty.Last.Arguments.Should().NotContain("--profile");
        files.Written.Should().BeEmpty();
        files.External.Should().BeEmpty();
    }

    // The profile is the only place ACT declares its hooks, so the assertions read it — which means
    // reading TOML back. Each event's command ends with that event's name, which is what makes one
    // line findable without parsing the whole document.
    private static string CommandFor(string profile, string eventName)
    {
        var line = profile.ReplaceLineEndings("\n").Split('\n')
            .Single(text => text.StartsWith("command = \"", StringComparison.Ordinal)
                && text.TrimEnd().EndsWith($" {eventName}\"", StringComparison.Ordinal));

        return Unescape(line["command = \"".Length..].TrimEnd()[..^1]);
    }

    // A TOML basic string, undone: a backslash escapes whatever follows it, so the pair collapses to
    // that character. Written out rather than chained `Replace` calls, which get the overlapping
    // `\\"` case wrong.
    private static string Unescape(string value)
    {
        var text = new System.Text.StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is '\\' && index + 1 < value.Length)
                index++;

            text.Append(value[index]);
        }

        return text.ToString();
    }

    private static Task<IAgentSession> LaunchAsync(
        StubPtyHost pty,
        IHookEndpoint endpoint,
        IAgentConfigFiles files,
        Guid? taskId = null)
        => new CodexAdapter(pty, new StubCommandHost(), new TestClock(), endpoint, files).LaunchAsync(new AgentLaunchRequest(
            taskId ?? TaskId,
            "ignored-by-codex",
            "C:/repo",
            "do the thing",
            new LaunchConfig(),
            TerminalSize.Default));
}

// Written against the documented contract and since **confirmed against real payloads** (2026-07-31):
// `SessionStart`, `UserPromptSubmit`, the tool events, `PermissionRequest`, `Stop` and
// `request_user_input` all normalized correctly from live runs.
public class CodexHookNormalizerTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 29, 22, 0, 0, TimeSpan.Zero);

    private readonly CodexHookNormalizer normalizer = new();

    // How a Codex card gets bound at all: ACT cannot pre-mint the id, so the first payload naming
    // the session is the binding.
    [Fact]
    public void The_payloads_session_id_is_reported_so_the_card_can_bind()
        => Normalize("""
            { "hook_event_name": "SessionStart", "session_id": "019f-abc", "cwd": "C:/repo" }
            """)
            .SessionId.Should().Be("019f-abc");

    [Fact]
    public void A_permission_request_is_an_explicit_event_keyed_by_turn()
    {
        var result = Normalize("""
            {
              "hook_event_name": "PermissionRequest",
              "session_id": "abc",
              "turn_id": "turn-7",
              "reason": "run a shell command"
            }
            """);

        var permission = result.Events.Should().ContainSingle().Which.Should().BeOfType<PermissionRequested>().Which;

        permission.RequestId.Should().Be("turn-7");
        permission.Summary.Should().Be("run a shell command");
    }

    // Codex's own ask-the-user tool. Every other tool means the session is working; this one means it
    // has stopped, so classifying it as activity would leave a blocked card sitting in Executing
    // reporting `running` — the same defect `AskUserQuestion` caused for Claude Code.
    //
    // ✅ Verified live on card #1097 by typing `/plan` into ACT's own terminal — the only way in, since
    // `request_user_input` is offered solely in Codex's **Plan collaboration mode**, which is a TUI
    // slash command and not a config key (`collaboration_mode`, `mode` and `collaboration` were all
    // type-probed and none exist). The real payload put its text in a **`questions` array**, each entry
    // carrying a `question`; `prompt` and `question` stay as fallbacks.
    [Fact]
    public void A_request_for_user_input_is_a_question_rather_than_activity()
    {
        var result = Normalize("""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "request_user_input",
              "tool_call_id": "call-3",
              "tool_input": { "prompt": "English or French?" }
            }
            """);

        var question = result.Events.Should().ContainSingle().Which.Should().BeOfType<QuestionAsked>().Which;

        question.RequestId.Should().Be("call-3");
        question.Question.Should().Be("English or French?");
    }

    [Fact]
    public void Any_other_tool_stays_activity_carrying_its_name()
        => Normalize("""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "shell_command"
            }
            """)
            .Events.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityObserved>()
            .Which.ToolName.Should().Be("shell_command");

    [Fact]
    public void Compaction_is_reported_at_both_ends()
    {
        Normalize("""{ "hook_event_name": "PreCompact", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<CompactingStarted>();

        Normalize("""{ "hook_event_name": "PostCompact", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<CompactingFinished>();
    }

    [Fact]
    public void An_unknown_event_normalizes_to_nothing_rather_than_failing()
        => Normalize("""{ "hook_event_name": "SubagentStart", "session_id": "abc" }""")
            .Events.Should().BeEmpty();

    private HookNormalization Normalize(string json)
        => normalizer.Normalize(JsonDocument.Parse(json).RootElement, null, At);
}
