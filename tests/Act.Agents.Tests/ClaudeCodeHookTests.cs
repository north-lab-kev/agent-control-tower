using System.Text.Json;
using Act.Agents.ClaudeCode;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;

namespace Act.Agents.Tests;

public class ClaudeCodeHookInjectionTests
{
    private static readonly Guid TaskId = Guid.Parse("6f0d5d5c-16b8-4a2c-9f4d-2f0a3f7c1e11");

    private const string SessionId = "39694aa9-918e-471b-8623-61d0d948ffbb";

    [Fact]
    public async Task A_launch_passes_its_own_settings_file_by_path()
    {
        var pty = new StubPtyHost();
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(pty, new StubHookEndpoint(), files);

        var arguments = pty.Last.Arguments.ToList();
        var index = arguments.IndexOf("--settings");

        index.Should().BeGreaterThanOrEqualTo(0);
        arguments[index + 1].Should().EndWith(ClaudeCodeHookSettings.FileName);
        files.Content(ClaudeCodeHookSettings.FileName).Should().NotBeNull();
    }

    // The user's own `.claude/settings.json` is theirs. ACT passes a path and never merges.
    [Fact]
    public async Task The_settings_file_carries_http_hooks_and_the_token_header()
    {
        var pty = new StubPtyHost();
        var files = new StubAgentConfigFiles();
        var endpoint = new StubHookEndpoint();

        await using var session = await LaunchAsync(pty, endpoint, files);

        var settings = JsonDocument.Parse(files.Content(ClaudeCodeHookSettings.FileName)!);
        var hook = settings.RootElement
            .GetProperty("hooks")
            .GetProperty("Notification")[0]
            .GetProperty("hooks")[0];

        hook.GetProperty("type").GetString().Should().Be("http");
        hook.GetProperty("url").GetString().Should().Be(endpoint.UrlFor(AgentType.ClaudeCode)!.ToString());
        hook.GetProperty("headers")
            .GetProperty(HookTransport.TokenHeader)
            .GetString()
            .Should().Be(endpoint.Register(TaskId));
    }

    // The signal that reports a waiting permission prompt. Nothing else does, now that ACT does
    // not read the screen — so its absence would be silent.
    [Fact]
    public async Task The_settings_file_subscribes_to_notification()
    {
        var pty = new StubPtyHost();
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(pty, new StubHookEndpoint(), files);

        files.Content(ClaudeCodeHookSettings.FileName).Should().Contain("Notification");
    }

    [Fact]
    public async Task The_token_and_endpoint_ride_the_environment_not_the_command_line()
    {
        var pty = new StubPtyHost();
        var endpoint = new StubHookEndpoint();

        await using var session = await LaunchAsync(pty, endpoint, new StubAgentConfigFiles());

        var token = endpoint.Register(TaskId);

        pty.Last.Environment.Should().ContainKey(AgentEnvironment.ActHookToken).WhoseValue.Should().Be(token);
        pty.Last.Environment.Should().ContainKey(AgentEnvironment.ActHookEndpoint)
            .WhoseValue.Should().Be(endpoint.UrlFor(AgentType.ClaudeCode)!.ToString());
        pty.Last.Arguments.Should().NotContain(argument => argument.Contains(token));
    }

    // Ingestion is observability. Losing it must never cost the user their session.
    [Fact]
    public async Task With_no_endpoint_the_launch_still_happens_without_hooks()
    {
        var pty = new StubPtyHost();
        var files = new StubAgentConfigFiles();

        await using var session = await LaunchAsync(pty, StubHookEndpoint.Offline(), files);

        pty.Last.Arguments.Should().NotContain("--settings");
        files.Written.Should().BeEmpty();
        pty.Last.Environment.Should().NotContainKey(AgentEnvironment.ActHookToken);
    }

    private static Task<IAgentSession> LaunchAsync(
        StubPtyHost pty,
        IHookEndpoint endpoint,
        IAgentConfigFiles files)
        => new ClaudeCodeAdapter(pty, new TestClock(), endpoint, files).LaunchAsync(new AgentLaunchRequest(
            TaskId,
            SessionId,
            "C:/repo",
            AgentPreamble.Compose(TaskId),
            "do the thing",
            new LaunchConfig(),
            TerminalSize.Default));
}

public class ClaudeCodeHookNormalizerTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 29, 22, 0, 0, TimeSpan.Zero);

    private readonly ClaudeCodeHookNormalizer normalizer = new();

    [Fact]
    public void Session_start_reports_the_transcript_and_directory()
    {
        var result = Normalize("""
            {
              "hook_event_name": "SessionStart",
              "session_id": "abc",
              "cwd": "C:/repo",
              "transcript_path": "C:/t.jsonl"
            }
            """);

        result.SessionId.Should().Be("abc");
        result.Events.Should().ContainSingle().Which.Should().BeOfType<SessionStarted>()
            .Which.TranscriptPath.Should().Be("C:/t.jsonl");
    }

    [Fact]
    public void A_tool_use_is_activity_and_carries_the_tool_name()
    {
        var result = Normalize("""
            { "hook_event_name": "PreToolUse", "session_id": "abc", "tool_name": "Bash" }
            """);

        result.Events.Should().ContainSingle().Which.Should().BeOfType<ActivityObserved>()
            .Which.ToolName.Should().Be("Bash");
    }

    // A prompt submitted is how ACT learns a blocked card recovered, since the user acts in the
    // terminal and never tells ACT directly.
    [Fact]
    public void A_submitted_prompt_is_activity()
        => Normalize("""{ "hook_event_name": "UserPromptSubmit", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<ActivityObserved>();

    // `Stop` says the turn ended, not how. The outcome comes from the status file.
    [Fact]
    public void Stop_ends_the_turn_with_an_unknown_outcome()
        => Normalize("""{ "hook_event_name": "Stop", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<TurnEnded>()
            .Which.Outcome.Should().Be(TurnOutcome.Unknown);

    [Fact]
    public void A_notification_about_permission_is_reported_as_a_waiting_prompt()
    {
        var result = Normalize("""
            {
              "hook_event_name": "Notification",
              "session_id": "abc",
              "message": "Claude needs your permission to use Bash"
            }
            """);

        result.Events.Should().ContainSingle().Which.Should().BeOfType<PermissionRequested>()
            .Which.Summary.Should().Contain("permission");
    }

    // Measured live on 2026-07-30: `Notification` also fires an idle nudge with exactly this
    // message about a minute after a turn ends. It must produce nothing. Treating it as a
    // permission request overwrites a reviewable card's `to review` with `needs permission` and
    // nothing to approve; treating it as activity is worse, because an idle nudge means the
    // opposite of activity and would send a reviewed card back to Executing on its own.
    [Fact]
    public void The_idle_nudge_is_not_a_permission_prompt_and_produces_nothing()
        => Normalize("""
            {
              "hook_event_name": "Notification",
              "session_id": "abc",
              "message": "Claude is waiting for your input"
            }
            """)
            .Events.Should().BeEmpty();

    [Fact]
    public void An_unrecognised_notification_produces_nothing_rather_than_a_guess()
        => Normalize("""
            { "hook_event_name": "Notification", "session_id": "abc", "message": "still working" }
            """)
            .Events.Should().BeEmpty();

    [Fact]
    public void An_unknown_event_normalizes_to_nothing_rather_than_failing()
        => Normalize("""{ "hook_event_name": "SomethingNewInTheCli", "session_id": "abc" }""")
            .Events.Should().BeEmpty();

    [Fact]
    public void A_payload_that_is_not_an_object_normalizes_to_nothing()
        => normalizer.Normalize(default, null, At).Events.Should().BeEmpty();

    // Claude Code takes a pre-minted id, so ACT already knows it; a payload that omits it must not
    // produce events attributed to an empty session.
    [Fact]
    public void The_known_session_id_is_used_when_the_payload_omits_one()
        => normalizer
            .Normalize(Parse("""{ "hook_event_name": "UserPromptSubmit" }"""), "known", At)
            .Events.Should().ContainSingle().Which.SessionId.Should().Be("known");

    private HookNormalization Normalize(string json) => normalizer.Normalize(Parse(json), null, At);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
