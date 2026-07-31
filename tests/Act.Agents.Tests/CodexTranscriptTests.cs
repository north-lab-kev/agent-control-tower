using Act.Agents.Codex;
using Act.Core.Events;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// Line shapes measured from real rollout files on 2026-07-31 (`codex-cli 0.146.0-alpha.3.1`), not
// taken from documentation — unlike `CodexHookTests`, which is still written blind because no hook has
// ever fired. This is the only thing that reports a Codex session today.
public class CodexTranscriptTests
{
    private const string Meta = """
        { "timestamp": "2026-07-30T02:38:41.625Z", "type": "session_meta",
          "payload": { "session_id": "019fb0e3-694f-7e30-afd5-ff1562e3a1d4",
                       "cwd": "C:\\dev\\act", "originator": "codex_exec" } }
        """;

    private const string TurnStarted = """
        { "timestamp": "2026-07-30T02:38:41.700Z", "type": "event_msg",
          "payload": { "type": "task_started", "turn_id": "t1", "model_context_window": 258400 } }
        """;

    private const string ToolCall = """
        { "timestamp": "2026-07-30T02:38:42.100Z", "type": "response_item",
          "payload": { "type": "function_call", "name": "shell_command", "call_id": "c1" } }
        """;

    private const string Tokens = """
        { "timestamp": "2026-07-30T02:38:42.400Z", "type": "event_msg",
          "payload": { "type": "token_count",
            "info": { "total_token_usage": { "input_tokens": 13330, "cached_input_tokens": 12672,
                                             "output_tokens": 55, "total_tokens": 13385 },
                      "last_token_usage": { "input_tokens": 13330, "cached_input_tokens": 12672,
                                            "output_tokens": 55 },
                      "model_context_window": 258400 } } }
        """;

    private const string TurnDone = """
        { "timestamp": "2026-07-30T02:38:42.756Z", "type": "event_msg",
          "payload": { "type": "task_complete", "turn_id": "t1",
                       "last_agent_message": "Read the file and stopped.", "duration_ms": 1140 } }
        """;

    private readonly CodexTranscriptNormalizer normalizer = new();

    // The whole reason this class emits events at all: with hooks dormant, a turn that ended is
    // something only the rollout can report.
    [Fact]
    public void A_completed_task_ends_the_turn()
        => Events(Meta, TurnStarted, TurnDone).Should().ContainItemsAssignableTo<AgentEvent>()
            .And.ContainSingle(observed => observed is TurnEnded);

    [Fact]
    public void A_started_task_is_activity_so_the_card_goes_back_to_running()
        => Events(Meta, TurnStarted).Should().ContainSingle()
            .Which.Should().BeOfType<ActivityObserved>();

    [Fact]
    public void A_function_call_is_activity_carrying_the_tool_name()
        => Events(Meta, ToolCall).Should().ContainSingle()
            .Which.Should().BeOfType<ActivityObserved>()
            .Which.ToolName.Should().Be("shell_command");

    // Published events carry the session id the file itself names, which for Codex is the only place
    // it exists — there is no `--session-id` to pre-mint.
    [Fact]
    public void Events_carry_the_session_id_out_of_the_meta_line()
        => Events(Meta, TurnStarted).Should().ContainSingle()
            .Which.SessionId.Should().Be("019fb0e3-694f-7e30-afd5-ff1562e3a1d4");

    [Fact]
    public void Timestamps_come_from_the_line_rather_than_the_clock()
        => Events(Meta, TurnDone).Should().ContainSingle()
            .Which.At.Should().Be(DateTimeOffset.Parse("2026-07-30T02:38:42.756Z"));

    // The CLI's own report that the turn failed while its process stays alive and would exit zero.
    // Measured on a real rollout: an account rejecting a model produced exactly this.
    [Fact]
    public void A_task_that_completed_with_an_error_fails_the_turn()
    {
        var failed = """
            { "timestamp": "2026-07-30T02:38:42.756Z", "type": "event_msg",
              "payload": { "type": "task_complete", "turn_id": "t1", "last_agent_message": null,
                "error": { "message": "{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'gpt-5.4' model is not supported when using Codex with a ChatGPT account.\"}}",
                           "codex_error_info": "other" } } }
            """;

        var turn = Events(Meta, failed).Should().ContainSingle()
            .Which.Should().BeOfType<TurnFailed>().Subject;

        // The envelope is json-in-json; what a card shows is the sentence, not the wrapper.
        turn.Reason.Should().Be(
            "The 'gpt-5.4' model is not supported when using Codex with a ChatGPT account.");
    }

    // Same rule as Claude Code, for the same reason: a resumed prefix re-read every turn is not work.
    [Fact]
    public void Tokens_are_cumulative_and_exclude_the_cached_prefix()
    {
        var snapshot = Fold(Meta, Tokens);

        snapshot.TokensIn.Should().Be(658);
        snapshot.TokensOut.Should().Be(55);
    }

    [Fact]
    public void Context_is_the_last_requests_input_cache_included()
        => Fold(Meta, Tokens).ContextUsed.Should().Be(13_330);

    // Codex reports its own window, so ACT needs no per-model table for it.
    [Fact]
    public void The_context_limit_is_observed_rather_than_looked_up()
        => Fold(Meta, Tokens).ContextLimit.Should().Be(258_400);

    [Fact]
    public void The_window_is_also_taken_off_a_started_task()
        => Fold(Meta, TurnStarted).ContextLimit.Should().Be(258_400);

    // Absolute, not incremented: a catch-up read of a session already in progress has to land on the
    // right numbers rather than doubling what was stored.
    [Fact]
    public void Turns_and_tool_calls_are_counted_absolutely()
    {
        var snapshot = Fold(Meta, TurnStarted, ToolCall, ToolCall, TurnDone, TurnStarted, TurnDone);

        snapshot.TurnCount.Should().Be(2);
        snapshot.ToolCalls.Should().Be(2);
    }

    [Fact]
    public void A_second_fold_continues_the_counts_rather_than_restarting_them()
    {
        var first = Fold(Meta, ToolCall, TurnDone);
        var second = normalizer.Fold(first, [ToolCall, TurnDone]).Snapshot;

        second.TurnCount.Should().Be(2);
        second.ToolCalls.Should().Be(2);
    }

    [Fact]
    public void The_last_agent_message_is_what_the_card_previews()
        => Fold(Meta, TurnDone).LastMessage.Should().Be("Read the file and stopped.");

    [Fact]
    public void An_unparseable_line_is_skipped_without_losing_the_rest()
        => Fold(Meta, "{ not json", Tokens).ContextUsed.Should().Be(13_330);

    [Fact]
    public void Lines_that_say_nothing_ACT_cares_about_produce_nothing()
    {
        var noise = """
            { "timestamp": "2026-07-30T02:38:41.900Z", "type": "world_state", "payload": { "type": "x" } }
            """;

        var fold = normalizer.Fold(new EnrichmentSnapshot(), [noise]);

        fold.Events.Should().BeEmpty();
        fold.Snapshot.Should().Be(new EnrichmentSnapshot { TurnCount = 0, ToolCalls = 0 });
    }

    private IReadOnlyList<AgentEvent> Events(params string[] lines)
        => normalizer.Fold(new EnrichmentSnapshot(), lines).Events;

    private EnrichmentSnapshot Fold(params string[] lines)
        => normalizer.Fold(new EnrichmentSnapshot(), lines).Snapshot;
}
