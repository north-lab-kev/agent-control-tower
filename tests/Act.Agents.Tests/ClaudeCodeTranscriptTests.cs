using Act.Agents.ClaudeCode;
using Act.Core.Events;
using AwesomeAssertions;

namespace Act.Agents.Tests;

// The line shapes here are the ones measured on a real transcript (`claude-code v2.1.220`), not
// invented: one json object per line, `assistant` lines carrying `message.model`, `message.content`
// and `message.usage`.
public class ClaudeCodeTranscriptTests
{
    private const string Answered = """
        { "type": "assistant", "message": { "model": "claude-opus-5",
          "content": [ { "type": "text", "text": "Done." } ],
          "usage": { "input_tokens": 2, "cache_creation_input_tokens": 1026,
                     "cache_read_input_tokens": 171945, "output_tokens": 465 } } }
        """;

    private readonly ClaudeCodeTranscriptNormalizer normalizer = new();

    [Fact]
    public void An_assistant_line_reports_the_model_it_actually_ran()
        => Fold(Answered).ObservedModel.Should().Be("claude-opus-5");

    // Fresh work only. A long session re-reads the same cached prefix on every request, so summing
    // cache reads would report tens of millions of tokens for an hour of work.
    [Fact]
    public void Tokens_count_fresh_input_and_output_and_exclude_cache_reads()
    {
        var snapshot = Fold(Answered);

        snapshot.TokensIn.Should().Be(1028);
        snapshot.TokensOut.Should().Be(465);
    }

    // The other half of the same usage block, answering a different question: how full the window
    // was for that request — which is the number the card's context bar shows.
    [Fact]
    public void Context_is_the_whole_of_the_last_request_cache_reads_included()
        => Fold(Answered).ContextUsed.Should().Be(172_973);

    [Fact]
    public void Tokens_accumulate_across_lines()
    {
        var snapshot = Fold(Answered, Answered);

        snapshot.TokensIn.Should().Be(2056);
        snapshot.TokensOut.Should().Be(930);
    }

    // Context is a level, not a total, and this is what makes it fall after a compaction rather than
    // climbing forever.
    [Fact]
    public void Context_is_replaced_rather_than_accumulated()
    {
        var compacted = """
            { "type": "assistant", "message": { "model": "claude-opus-5", "content": [],
              "usage": { "input_tokens": 12, "cache_read_input_tokens": 8000, "output_tokens": 40 } } }
            """;

        Fold(Answered, compacted).ContextUsed.Should().Be(8012);
    }

    // The hooks count both first-hand, and a second producer would fight the first for the same field.
    // True of Codex too since its hooks were fixed — `CodexTranscriptNormalizer` leaves them null for
    // exactly this reason.
    [Fact]
    public void Turns_and_tool_calls_are_left_to_the_hooks()
    {
        var snapshot = Fold(Answered, Answered);

        snapshot.TurnCount.Should().BeNull();
        snapshot.ToolCalls.Should().BeNull();
    }

    [Fact]
    public void Everything_that_is_not_an_assistant_line_is_ignored()
    {
        var lines = new[]
        {
            """{ "type": "user", "message": { "role": "user", "content": [] } }""",
            """{ "type": "ai-title", "aiTitle": "Something", "sessionId": "abc" }""",
            """{ "type": "queue-operation", "operation": "add" }""",
        };

        Fold(lines).Should().Be(new EnrichmentSnapshot());
    }

    // A transcript is someone else's format, written while ACT reads it. A line ACT cannot parse is
    // skipped, and must not cost what the lines around it said.
    [Fact]
    public void An_unparseable_line_is_skipped_without_losing_the_rest()
    {
        var snapshot = Fold(Answered, "{ not json", Answered);

        snapshot.TokensIn.Should().Be(2056);
        snapshot.ObservedModel.Should().Be("claude-opus-5");
    }

    [Fact]
    public void An_assistant_line_with_no_usage_still_reports_the_model()
    {
        var noUsage = """
            { "type": "assistant", "message": { "model": "claude-opus-5", "content": [] } }
            """;

        var snapshot = Fold(noUsage);

        snapshot.ObservedModel.Should().Be("claude-opus-5");
        snapshot.TokensIn.Should().Be(0);
        snapshot.ContextUsed.Should().BeNull();
    }

    // Claude Code's hooks report liveness first-hand, so the fold has no business emitting events.
    [Fact]
    public void The_fold_emits_no_events_because_the_hooks_already_did()
        => normalizer.Fold(new EnrichmentSnapshot(), [Answered]).Events.Should().BeEmpty();

    private EnrichmentSnapshot Fold(params string[] lines)
        => normalizer.Fold(new EnrichmentSnapshot(), lines).Snapshot;
}
