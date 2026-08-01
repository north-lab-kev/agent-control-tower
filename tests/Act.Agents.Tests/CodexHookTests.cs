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
    [Fact]
    public async Task The_profile_declares_every_event_the_hooks_file_does()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var profile = files.External.Single().Value;
        var declared = JsonDocument.Parse(files.Content(CodexHookConfig.HooksFileName)!)
            .RootElement.GetProperty("hooks")
            .EnumerateObject()
            .Select(property => property.Name);

        foreach (var name in declared)
            profile.Should().Contain($"[[hooks.{name}]]");
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

        var command = JsonDocument.Parse(files.Content(CodexHookConfig.HooksFileName)!)
            .RootElement.GetProperty("hooks")
            .GetProperty("SessionStart")[0]
            .GetProperty("hooks")[0]
            .GetProperty("command")
            .GetString()!;

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

        var command = JsonDocument.Parse(files.Content(CodexHookConfig.HooksFileName)!)
            .RootElement.GetProperty("hooks")
            .GetProperty("Stop")[0]
            .GetProperty("hooks")[0]
            .GetProperty("command")
            .GetString()!;

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

    // The definition is hashed for trust, so the profile and the json must not drift: two spellings
    // of the same handler would be two hashes and a second review prompt.
    [Fact]
    public async Task The_profile_and_the_hooks_file_declare_the_same_command()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var command = JsonDocument.Parse(files.Content(CodexHookConfig.HooksFileName)!)
            .RootElement.GetProperty("hooks")
            .GetProperty("SessionStart")[0]
            .GetProperty("hooks")[0]
            .GetProperty("command")
            .GetString()!;

        // The same command, spelled for TOML: separators and quotes both escaped.
        var escaped = command.Replace("\\", "\\\\").Replace("\"", "\\\"");

        files.External.Single().Value.Should().Contain(escaped);
    }

    [Fact]
    public async Task The_hooks_file_declares_command_handlers_per_event()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var document = JsonDocument.Parse(files.Content(CodexHookConfig.HooksFileName)!);
        var events = document.RootElement.GetProperty("hooks");

        var handler = events.GetProperty("PermissionRequest")[0].GetProperty("hooks")[0];

        handler.GetProperty("type").GetString().Should().Be("command");
        handler.GetProperty("command").GetString().Should().Contain("PermissionRequest");
    }

    // The one Codex has and Claude Code does not: an explicit event for a waiting prompt, rather
    // than a notification message to guess from.
    [Fact]
    public async Task The_hooks_file_subscribes_to_permission_request()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        files.Content(CodexHookConfig.HooksFileName).Should().Contain("PermissionRequest");
    }

    // The load-bearing property of the whole design: Codex hashes each handler definition and
    // re-prompts for trust when it changes. Two launches of the same card — and of a different one
    // — must produce byte-identical hook json, which is only possible while the token and the
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

        var a = first.Content(CodexHookConfig.HooksFileName)!;
        var b = second.Content(CodexHookConfig.HooksFileName)!;

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

    private static Task<IAgentSession> LaunchAsync(
        StubPtyHost pty,
        IHookEndpoint endpoint,
        IAgentConfigFiles files,
        Guid? taskId = null)
        => new CodexAdapter(pty, new TestClock(), endpoint, files).LaunchAsync(new AgentLaunchRequest(
            taskId ?? TaskId,
            "ignored-by-codex",
            "C:/repo",
            "do the thing",
            new LaunchConfig(),
            TerminalSize.Default));
}

// PENDING SUBJECT — no real Codex payload has ever been captured; these encode the documented
// contract so a future session can compare them against a live one.
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
