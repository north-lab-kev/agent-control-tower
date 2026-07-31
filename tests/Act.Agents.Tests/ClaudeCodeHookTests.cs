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

    // The tool that asks the user is the one `PreToolUse` must not report as activity: the agent has
    // stopped, and this payload is the only one carrying what it asked.
    [Fact]
    public void The_question_tool_is_reported_as_a_question_with_what_it_asked()
    {
        var result = Normalize("""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "AskUserQuestion",
              "tool_use_id": "toolu_01",
              "tool_input": {
                "questions": [
                  { "question": "Pick a number between 1 and 3.", "header": "Pick a number" }
                ]
              }
            }
            """);

        var asked = result.Events.Should().ContainSingle().Which.Should().BeOfType<QuestionAsked>().Subject;

        asked.Question.Should().Be("Pick a number between 1 and 3.");
        asked.RequestId.Should().Be("toolu_01");
    }

    [Fact]
    public void Several_questions_in_one_call_are_all_reported()
        => Normalize("""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "AskUserQuestion",
              "tool_input": { "questions": [{ "question": "Which store?" }, { "question": "Which port?" }] }
            }
            """)
            .Events.Should().ContainSingle().Which.Should().BeOfType<QuestionAsked>()
            .Which.Question.Should().Be("Which store? · Which port?");

    // The badge is the point, so a payload ACT cannot read the question out of still has to produce
    // the question event rather than fall back to activity.
    [Fact]
    public void A_question_with_no_readable_input_is_still_a_question()
        => Normalize("""
            { "hook_event_name": "PreToolUse", "session_id": "abc", "tool_name": "AskUserQuestion" }
            """)
            .Events.Should().ContainSingle().Which.Should().BeOfType<QuestionAsked>()
            .Which.Question.Should().BeEmpty();

    // The tool finishing means the user answered it, in the terminal, where the answer lives.
    [Fact]
    public void The_question_tool_finishing_is_activity()
        => Normalize("""
            { "hook_event_name": "PostToolUse", "session_id": "abc", "tool_name": "AskUserQuestion" }
            """)
            .Events.Should().ContainSingle().Which.Should().BeOfType<ActivityObserved>();

    // A prompt submitted is how ACT learns a blocked card recovered, since the user acts in the
    // terminal and never tells ACT directly.
    [Fact]
    public void A_submitted_prompt_is_activity()
        => Normalize("""{ "hook_event_name": "UserPromptSubmit", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<ActivityObserved>();

    [Fact]
    public void Stop_ends_the_turn()
        => Normalize("""{ "hook_event_name": "Stop", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<TurnEnded>();

    // Measured live on 2026-07-30 against `claude-code v2.1.220`, and the type is what ACT keys on:
    // the wording is shared with the question tool's prompt, the type is not shared with the idle
    // nudge.
    [Fact]
    public void A_permission_prompt_notification_is_reported_as_a_waiting_prompt()
    {
        var result = Normalize("""
            {
              "hook_event_name": "Notification",
              "session_id": "abc",
              "message": "Claude needs your permission",
              "notification_type": "permission_prompt"
            }
            """);

        result.Events.Should().ContainSingle().Which.Should().BeOfType<PermissionRequested>()
            .Which.Summary.Should().Contain("permission");
    }

    // Measured live on the same build: `Notification` also fires an idle nudge about a minute after
    // a turn ends. It must produce nothing. Treating it as a permission request overwrites a
    // reviewable card's `to review` with `needs permission` and nothing to approve; treating it as
    // activity is worse, because an idle nudge means the opposite of activity and would send a
    // reviewed card back to Executing on its own.
    [Fact]
    public void The_idle_nudge_is_not_a_permission_prompt_and_produces_nothing()
        => Normalize("""
            {
              "hook_event_name": "Notification",
              "session_id": "abc",
              "message": "Claude is waiting for your input",
              "notification_type": "idle_prompt"
            }
            """)
            .Events.Should().BeEmpty();

    // Permission wording on an unknown type is still an unknown notification. The type is the
    // signal, and guessing from the message is what the question tool's prompt already defeats.
    [Fact]
    public void An_unrecognised_notification_type_produces_nothing_rather_than_a_guess()
        => Normalize("""
            {
              "hook_event_name": "Notification",
              "session_id": "abc",
              "message": "Claude needs your permission",
              "notification_type": "something_new"
            }
            """)
            .Events.Should().BeEmpty();

    // A CLI that stops sending the type must not stop reporting permission prompts.
    [Fact]
    public void With_no_type_the_message_still_classifies_the_notification()
    {
        Normalize("""
            {
              "hook_event_name": "Notification",
              "session_id": "abc",
              "message": "Claude needs your permission"
            }
            """)
            .Events.Should().ContainSingle().Which.Should().BeOfType<PermissionRequested>();

        Normalize("""
            { "hook_event_name": "Notification", "session_id": "abc", "message": "still working" }
            """)
            .Events.Should().BeEmpty();
    }

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
