using System.Text.Json;
using Act.Agents.Codex;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// PENDING SUBJECT — these pin what ACT *writes*, which is all that can be tested while Codex
// hooks do not fire at all (see `docs/codex-hooks-findings.md`). They will keep passing whether or
// not the CLI ever runs the hooks, so a green suite here is not evidence that ingestion works.
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

    // `hooks` is a string path to a json file, not a `[[hooks.*]]` table. Getting this wrong is
    // what the findings caught: a table fails with "invalid type: sequence, expected a string".
    [Fact]
    public async Task The_profile_points_at_a_hooks_file_by_path()
    {
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(new StubPtyHost(), new StubHookEndpoint(), files);

        var profile = files.External.Should().ContainSingle().Which;

        profile.Key.Should().EndWith($"{CodexHookConfig.ProfileName}.config.toml");
        profile.Value.Should().StartWith("#").And.Contain("hooks = \"");
        profile.Value.Should().Contain(CodexHookConfig.HooksFileName.Replace(".", "."));
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
            AgentPreamble.Compose(taskId ?? TaskId),
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
