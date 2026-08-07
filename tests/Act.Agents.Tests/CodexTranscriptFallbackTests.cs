using Act.Agents.Codex;
using Act.Core.Events;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// The shapes a rollout can hold that the happy-path suite does not reach. A transcript is a file the
// CLI owns and rewrites between versions, so every one of these has to fold to "no news" rather than
// to a wrong number or a throw.
public class CodexTranscriptFallbackTests
{
    private readonly CodexTranscriptNormalizer normalizer = new();

    [Fact]
    public void The_normalizer_answers_for_codex()
        => normalizer.Agent.Should().Be(AgentType.Codex);

    // Null means "no news", not "zero": a `token_count` whose shape the CLI has moved must leave the
    // figures already on the card alone rather than reset them.
    [Theory]
    [InlineData("""{ "type": "token_count" }""")]
    [InlineData("""{ "type": "token_count", "info": null }""")]
    [InlineData("""{ "type": "token_count", "info": "a string" }""")]
    [InlineData("""{ "type": "token_count", "info": [] }""")]
    public void A_token_count_with_no_readable_info_changes_nothing(string message)
    {
        var known = new EnrichmentSnapshot { TokensIn = 900, TokensOut = 40, ContextUsed = 5_000 };

        Fold(known, Line(message)).Should().Be(known);
    }

    [Theory]
    [InlineData("""{ "type": "token_count", "info": { "total_token_usage": null } }""")]
    [InlineData("""{ "type": "token_count", "info": { "total_token_usage": 7 } }""")]
    [InlineData("""{ "type": "token_count", "info": { "last_token_usage": [] } }""")]
    public void A_usage_block_that_is_not_an_object_leaves_the_figures_alone(string message)
    {
        var known = new EnrichmentSnapshot { TokensIn = 900, TokensOut = 40, ContextUsed = 5_000 };

        Fold(known, Line(message)).Should().Be(known);
    }

    // Cached input is excluded from the token totals — a resumed prefix re-read every turn is not
    // work — but kept in the context figure, because it occupies the window all the same.
    [Fact]
    public void Cached_input_is_out_of_the_totals_and_in_the_context_figure()
    {
        var snapshot = Fold(new EnrichmentSnapshot(), Line("""
            {
              "type": "token_count",
              "info": {
                "total_token_usage": { "input_tokens": 5000, "cached_input_tokens": 4000, "output_tokens": 120 },
                "last_token_usage": { "input_tokens": 4500, "cached_input_tokens": 4000, "output_tokens": 20 }
              }
            }
            """));

        snapshot.TokensIn.Should().Be(1000);
        snapshot.TokensOut.Should().Be(120);
        snapshot.ContextUsed.Should().Be(4500);
    }

    // The message is json-in-json — an api error body the CLI captured verbatim. ACT stores the
    // sentence, not the envelope, and falls back to the envelope when it cannot find one.
    [Fact]
    public void A_nested_api_error_is_unwrapped_to_its_sentence()
        => Failure("""
            {
              "type": "task_complete",
              "error": { "message": "{\"error\":{\"message\":\"rate limit exceeded\"}}" }
            }
            """)
            .Should().Be("rate limit exceeded");

    [Fact]
    public void An_error_message_that_is_not_json_is_stored_as_it_stands()
        => Failure("""
            { "type": "task_complete", "error": { "message": "  the model refused  " } }
            """)
            .Should().Be("the model refused");

    // Valid json that is not the envelope ACT knows: the whole string is the sentence.
    [Fact]
    public void An_error_message_that_is_json_of_another_shape_is_stored_whole()
        => Failure("""
            { "type": "task_complete", "error": { "message": "{\"detail\":\"nope\"}" } }
            """)
            .Should().Be("""{"detail":"nope"}""");

    [Fact]
    public void An_error_with_no_message_falls_back_to_the_code_the_cli_gave()
        => Failure("""
            { "type": "task_complete", "error": { "codex_error_info": "stream_disconnected" } }
            """)
            .Should().Be("stream_disconnected");

    // Something failed and ACT could not read what: the card still has to say the turn failed.
    [Fact]
    public void An_error_with_nothing_readable_in_it_still_reports_a_failure()
        => Failure("""{ "type": "task_complete", "error": {} }""").Should().Be("failed");

    [Theory]
    [InlineData("""{ "type": "task_complete" }""")]
    [InlineData("""{ "type": "task_complete", "error": null }""")]
    [InlineData("""{ "type": "task_complete", "error": "boom" }""")]
    public void A_turn_that_completed_normally_raises_nothing(string message)
        => Events(Line(message)).Should().BeEmpty("a plain turn end is the Stop hook's to report");

    // Long enough to bury the rest of the card, and it is stored rather than rendered once — so it is
    // bounded on the way in.
    [Fact]
    public void A_very_long_failure_is_cut_rather_than_stored_whole()
    {
        var failure = Failure($$"""
            { "type": "task_complete", "error": { "message": "{{new string('x', 900)}}" } }
            """);

        failure.Should().HaveLength(400);
    }

    [Theory]
    [InlineData("""{ "type": "task_complete", "error": { "message": "   " } }""")]
    [InlineData("""{ "type": "task_complete", "error": { "message": "" } }""")]
    public void A_failure_that_is_only_whitespace_is_not_reported(string message)
        => Events(Line(message)).Should().BeEmpty();

    // Every one of these is a line shape the fold has to walk past rather than trip on.
    [Theory]
    [InlineData("""[1, 2, 3]""")]
    [InlineData(""""a string"""")]
    [InlineData("""{ "type": "event_msg" }""")]
    [InlineData("""{ "type": "event_msg", "payload": 7 }""")]
    [InlineData("""{ "type": "session_meta" }""")]
    [InlineData("""{ "payload": { "type": "token_count" } }""")]
    [InlineData("""{ "type": 7, "payload": {} }""")]
    public void A_line_ACT_cannot_read_is_walked_past(string line)
    {
        var fold = normalizer.Fold(new EnrichmentSnapshot(), [line]);

        fold.Events.Should().BeEmpty();
        fold.Snapshot.Should().Be(new EnrichmentSnapshot());
    }

    // The id the failure is reported under comes from `session_meta`, and `id` is the fallback the
    // field name is not worth a lost correlation over.
    [Theory]
    [InlineData("session_id")]
    [InlineData("id")]
    public void The_session_id_is_read_from_either_field_the_meta_line_may_use(string field)
    {
        var failed = Events(
                $$"""{ "type": "session_meta", "payload": { "{{field}}": "019f-abc" } }""",
                Line("""{ "type": "task_complete", "error": { "message": "boom" } }"""))
            .Should().ContainSingle().Which.Should().BeOfType<TurnFailed>().Which;

        failed.SessionId.Should().Be("019f-abc");
    }

    // A line with no readable timestamp still has to carry one, because a transition is stored with
    // the instant it happened.
    [Fact]
    public void A_line_with_no_usable_timestamp_falls_back_to_the_epoch()
        => Events("""
            { "type": "event_msg", "payload": { "type": "task_complete", "error": { "message": "boom" } } }
            """)
            .Should().ContainSingle().Which.At.Should().Be(DateTimeOffset.UnixEpoch);

    [Fact]
    public void An_unparseable_timestamp_falls_back_the_same_way()
        => Events("""
            {
              "timestamp": "not a date",
              "type": "event_msg",
              "payload": { "type": "task_complete", "error": { "message": "boom" } }
            }
            """)
            .Should().ContainSingle().Which.At.Should().Be(DateTimeOffset.UnixEpoch);

    private static string Line(string message)
        => $$"""
            { "timestamp": "2026-07-30T02:38:41.900Z", "type": "event_msg", "payload": {{message}} }
            """;

    private string? Failure(string message)
        => Events(Line(message)).Should().ContainSingle()
            .Which.Should().BeOfType<TurnFailed>().Which.Reason;

    private IReadOnlyList<AgentEvent> Events(params string[] lines)
        => normalizer.Fold(new EnrichmentSnapshot(), lines).Events;

    private EnrichmentSnapshot Fold(EnrichmentSnapshot snapshot, params string[] lines)
        => normalizer.Fold(snapshot, lines).Snapshot;
}
