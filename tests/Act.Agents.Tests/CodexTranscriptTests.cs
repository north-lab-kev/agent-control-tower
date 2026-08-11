using Act.Agents.Codex;
using Act.Core.Events;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// Line shapes measured from real rollout files (`codex-cli 0.146.0-alpha.3.1`), not
// taken from documentation.
//
// **Enrichment only.** Activity, turn ends and the turn/tool counts are the hooks' to report; what
// is asserted here is the same surface `ClaudeCodeTranscriptNormalizer` has — tokens, context, the
// window, the last message — plus the one event a hook has never been seen to report, `TurnFailed`.
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

    private const string Failed = """
        { "timestamp": "2026-07-30T02:38:42.756Z", "type": "event_msg",
          "payload": { "type": "task_complete", "turn_id": "t1", "last_agent_message": null,
            "error": { "message": "{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",\"message\":\"The 'gpt-5.4' model is not supported when using Codex with a ChatGPT account.\"}}",
                       "codex_error_info": "other" } } }
        """;

    private readonly CodexTranscriptNormalizer normalizer = new();

    // The hooks own liveness now, and a second reporter would move the card and count the turn twice.
    // These four line kinds are read for enrichment and must publish nothing at all.
    [Fact]
    public void Liveness_is_left_to_the_hooks_so_the_file_publishes_no_events()
        => Events(Meta, TurnStarted, ToolCall, Tokens, TurnDone).Should().BeEmpty();

    // The counts go with the events: `PreToolUse` and `Stop` count first-hand, exactly as Claude
    // Code's do, so leaving them null here is what stops two sources disagreeing.
    [Fact]
    public void Turn_and_tool_counts_are_left_to_the_hooks()
    {
        var snapshot = Fold(Meta, TurnStarted, ToolCall, ToolCall, TurnDone);

        snapshot.TurnCount.Should().BeNull();
        snapshot.ToolCalls.Should().BeNull();
    }

    // A count that arrived from a hook must survive a fold rather than being reset by it.
    [Fact]
    public void A_count_a_hook_already_reported_is_left_alone()
    {
        var counted = new EnrichmentSnapshot { TurnCount = 3, ToolCalls = 7 };

        var snapshot = normalizer.Fold(counted, [Meta, TurnStarted, ToolCall, TurnDone]).Snapshot;

        snapshot.TurnCount.Should().Be(3);
        snapshot.ToolCalls.Should().Be(7);
    }

    [Fact]
    public void A_failed_turn_carries_the_session_id_out_of_the_meta_line()
        => Events(Meta, Failed).Should().ContainSingle()
            .Which.SessionId.Should().Be("019fb0e3-694f-7e30-afd5-ff1562e3a1d4");

    [Fact]
    public void Timestamps_come_from_the_line_rather_than_the_clock()
        => Events(Meta, Failed).Should().ContainSingle()
            .Which.At.Should().Be(DateTimeOffset.Parse("2026-07-30T02:38:42.756Z"));

    // The one event this class still raises, and why it is the exception: the CLI reporting that the
    // turn failed while its process stays alive and would exit zero — invisible to `ProcessExited`, and
    // not yet observed on any hook payload. Measured on a real rollout: an account rejecting a model
    // produced exactly this.
    [Fact]
    public void A_task_that_completed_with_an_error_fails_the_turn()
    {
        var turn = Events(Meta, Failed).Should().ContainSingle()
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
        fold.Snapshot.Should().Be(new EnrichmentSnapshot());
    }

    private IReadOnlyList<AgentEvent> Events(params string[] lines)
        => normalizer.Fold(new EnrichmentSnapshot(), lines).Events;

    private EnrichmentSnapshot Fold(params string[] lines)
        => normalizer.Fold(new EnrichmentSnapshot(), lines).Snapshot;
}
