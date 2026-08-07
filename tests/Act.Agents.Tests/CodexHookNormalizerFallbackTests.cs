using System.Text.Json;
using Act.Agents.Codex;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// The events and the payload shapes the first suite does not reach. Every one of them is something a
// CLI upgrade can produce, and none of them may throw out of a hook: the endpoint answers a process
// that is mid-turn, and an exception there is a card that stops reporting.
public class CodexHookNormalizerFallbackTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 29, 22, 0, 0, TimeSpan.Zero);

    private readonly CodexHookNormalizer normalizer = new();

    [Fact]
    public void The_normalizer_answers_for_codex()
        => normalizer.Agent.Should().Be(AgentType.Codex);

    [Theory]
    [InlineData("\"a string\"")]
    [InlineData("[]")]
    [InlineData("7")]
    [InlineData("null")]
    public void A_payload_that_is_not_an_object_normalizes_to_nothing(string json)
        => Normalize(json).Should().Be(HookNormalization.None);

    // A payload with no event name is not nothing: it may still be the first thing that says where the
    // transcript is, which is the only way a tail ever starts.
    [Fact]
    public void A_payload_with_no_event_name_still_reports_the_transcript_and_the_session()
    {
        var result = Normalize("""
            { "session_id": "019f-abc", "transcript_path": "/sessions/019f-abc.jsonl" }
            """);

        result.SessionId.Should().Be("019f-abc");
        result.TranscriptPath.Should().Be("/sessions/019f-abc.jsonl");
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public void A_session_end_is_reported()
        => Normalize("""{ "hook_event_name": "SessionEnd", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<SessionEnded>();

    // The user typing is activity, which is what puts a parked card back into Executing.
    [Fact]
    public void A_submitted_prompt_is_activity()
        => Normalize("""{ "hook_event_name": "UserPromptSubmit", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<ActivityObserved>();

    [Fact]
    public void A_finished_tool_is_activity_carrying_its_name()
        => Normalize("""
            { "hook_event_name": "PostToolUse", "session_id": "abc", "tool_name": "apply_patch" }
            """)
            .Events.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityObserved>()
            .Which.ToolName.Should().Be("apply_patch");

    [Fact]
    public void A_stop_ends_the_turn()
        => Normalize("""{ "hook_event_name": "Stop", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<TurnEnded>();

    [Fact]
    public void A_session_start_carries_the_transcript_and_the_working_directory()
    {
        var started = Normalize("""
            {
              "hook_event_name": "SessionStart",
              "session_id": "abc",
              "transcript_path": "/sessions/abc.jsonl",
              "cwd": "C:/repo"
            }
            """)
            .Events.Should().ContainSingle().Which.Should().BeOfType<SessionStarted>().Which;

        started.TranscriptPath.Should().Be("/sessions/abc.jsonl");
        started.WorkingDir.Should().Be("C:/repo");
    }

    // A session start that names neither is still a session start; the fields become empty rather
    // than the event being dropped.
    [Fact]
    public void A_session_start_with_neither_field_is_still_a_session_start()
    {
        var started = Normalize("""{ "hook_event_name": "SessionStart", "session_id": "abc" }""")
            .Events.Should().ContainSingle().Which.Should().BeOfType<SessionStarted>().Which;

        started.TranscriptPath.Should().BeEmpty();
        started.WorkingDir.Should().BeEmpty();
    }

    // Measured live on card #1097: the real payload put its text in a `questions` array, each entry
    // carrying a `question`. `prompt` and `question` stay as fallbacks.
    [Fact]
    public void A_questions_array_is_joined_into_one_question()
        => Question("""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "request_user_input",
              "tool_call_id": "call-3",
              "tool_input": { "questions": [{ "question": "English?" }, { "prompt": "or French?" }] }
            }
            """)
            .Question.Should().Be("English? · or French?");

    [Fact]
    public void A_questions_array_of_plain_strings_is_joined_too()
        => Question("""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "request_user_input",
              "tool_input": { "questions": ["English?", "or French?"] }
            }
            """)
            .Question.Should().Be("English? · or French?");

    // The entry kinds are the point: a number or a null among them used to reach `TryGetProperty` on
    // an element that is not an object, which *throws* — out of a hook request, mid-turn.
    [Fact]
    public void Entries_that_say_nothing_are_left_out_of_the_joined_question()
        => Question("""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "request_user_input",
              "tool_input": { "questions": ["English?", "  ", { "other": "x" }, 7, null, []] }
            }
            """)
            .Question.Should().Be("English?");

    [Fact]
    public void The_singular_question_field_is_read_as_well_as_prompt()
        => Question("""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "request_user_input",
              "tool_input": { "question": "English or French?" }
            }
            """)
            .Question.Should().Be("English or French?");

    // Still a question — a card must not report `running` while the session waits — even when the
    // payload carries no text ACT can show. Written out in full rather than spliced together: every
    // one of these is a *shape*, and a helper that assembles them hides the shape being tested.
    [Theory]
    [InlineData("""{ "tool_input": {} }""")]
    [InlineData("""{ "tool_input": [] }""")]
    [InlineData("""{ "tool_input": "a string" }""")]
    [InlineData("""{ "tool_input": { "questions": {} } }""")]
    [InlineData("""{ "tool_input": { "questions": [] } }""")]
    [InlineData("""{ "tool_input": { "questions": ["  "] } }""")]
    [InlineData("""{ "tool_input": { "questions": [7, null, []] } }""")]
    [InlineData("""{ "tool_input": { "prompt": 7 } }""")]
    [InlineData("{}")]
    public void A_question_with_no_readable_text_is_still_a_question(string toolInput)
    {
        var payload = $$"""
            {
              "hook_event_name": "PreToolUse",
              "session_id": "abc",
              "tool_name": "request_user_input",
              "tool_call_id": "call-3",
              "extra": {{toolInput}}
            }
            """;

        Question(Flatten(payload)).Question.Should().BeEmpty("the badge is the point, not the text");
    }

    // Read-only and deliberately short: what the card shows is a statement that something is waiting,
    // not a command dump the user might mistake for something ACT can answer.
    [Theory]
    [InlineData("""{ "reason": "run a shell command" }""", "run a shell command")]
    [InlineData("""{ "message": "needs approval" }""", "needs approval")]
    [InlineData("""{ "tool_name": "apply_patch" }""", "apply_patch")]
    [InlineData("""{ "reason": 7, "tool_name": "apply_patch" }""", "apply_patch")]
    [InlineData("{}", "waiting for approval")]
    public void A_permission_request_falls_back_through_the_fields_it_may_carry(string fields, string summary)
    {
        var payload = $$"""
            {
              "hook_event_name": "PermissionRequest",
              "session_id": "abc",
              "turn_id": "turn-7",
              "extra": {{fields}}
            }
            """;

        Normalize(Flatten(payload)).Events.Should().ContainSingle()
            .Which.Should().BeOfType<PermissionRequested>()
            .Which.Summary.Should().Be(summary);
    }

    // Lifts whatever the `extra` object holds up beside its siblings, so a theory case can be one
    // well-formed json value instead of a fragment that has to be comma-spliced into a template.
    private static string Flatten(string payload)
    {
        var root = JsonDocument.Parse(payload).RootElement;

        var fields = root.EnumerateObject()
            .Where(property => property.Name is not "extra")
            .Concat(root.GetProperty("extra") is { ValueKind: JsonValueKind.Object } extra
                ? extra.EnumerateObject()
                : [])
            .Select(property => $"{JsonSerializer.Serialize(property.Name)}:{property.Value.GetRawText()}");

        return $"{{{string.Join(",", fields)}}}";
    }

    // Without a turn id there would be no key at all, and a request ACT cannot key is one it cannot
    // tell apart from the next one.
    [Fact]
    public void A_permission_request_with_no_turn_id_is_keyed_by_the_session_and_the_instant()
        => Normalize("""{ "hook_event_name": "PermissionRequest", "session_id": "abc" }""")
            .Events.Should().ContainSingle()
            .Which.Should().BeOfType<PermissionRequested>()
            .Which.RequestId.Should().Be($"abc:{At.ToUnixTimeMilliseconds()}");

    // ACT's own task id is the correlation handle until Codex reports its session, so a payload that
    // does not name one has to keep the id the endpoint already knew.
    [Fact]
    public void A_payload_with_no_session_id_keeps_the_one_already_known()
        => normalizer
            .Normalize(
                JsonDocument.Parse("""{ "hook_event_name": "Stop" }""").RootElement,
                "known-abc",
                At)
            .SessionId.Should().Be("known-abc");

    private QuestionAsked Question(string json)
        => Normalize(json).Events.Should().ContainSingle().Which.Should().BeOfType<QuestionAsked>().Which;

    private HookNormalization Normalize(string json)
        => normalizer.Normalize(JsonDocument.Parse(json).RootElement, null, At);
}
