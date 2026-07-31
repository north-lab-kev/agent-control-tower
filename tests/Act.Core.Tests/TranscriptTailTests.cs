using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class TranscriptTailTests
{
    private const string Path = "C:/t/session.jsonl";

    [Fact]
    public void The_first_read_starts_at_the_beginning_of_the_file()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("one");
        tail.Advance();

        reader.Offsets.Should().Equal([0L]);
    }

    // What makes a full first read safe to repeat every second: the offset the reader hands back is
    // where the next one starts, so an hour-old session is read once and tailed after that.
    [Fact]
    public void Every_read_after_it_starts_where_the_last_one_stopped()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("one");
        tail.Advance();
        reader.Give("two");
        tail.Advance();

        reader.Offsets.Should().Equal([0L, 3L]);
    }

    [Fact]
    public void News_is_published_as_a_snapshot()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("model=claude-opus-5");

        tail.Advance().Snapshot!.ObservedModel.Should().Be("claude-opus-5");
    }

    [Fact]
    public void Nothing_appended_is_not_news()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("model=claude-opus-5");
        tail.Advance();

        tail.Advance().IsNothing.Should().BeTrue();
    }

    // The reason the tail compares rather than publishes: `BoardState.UpdateAsync` re-reads every
    // card, so a snapshot identical to the last one would re-render the board once a second for news
    // that is not news.
    [Fact]
    public void Lines_that_change_nothing_are_not_news()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("model=claude-opus-5");
        tail.Advance();
        reader.Give("nothing-of-interest");

        tail.Advance().Snapshot.Should().BeNull();
    }

    // The catch-up read is the whole file, so its events are history: replaying them would move the
    // card on a turn that ended an hour ago and count every turn a second time. The snapshot survives,
    // because it says where the session *is* rather than what happened.
    [Fact]
    public void The_catch_up_read_keeps_the_snapshot_and_drops_the_history()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("model=claude-opus-5");
        reader.Give("turn");
        reader.Give("turn");

        var update = tail.Advance();

        update.Snapshot.Should().NotBeNull();
        update.Events.Should().BeEmpty();
    }

    [Fact]
    public void Events_after_the_catch_up_are_news()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("turn");
        tail.Advance();
        reader.Give("turn");

        tail.Advance().Events.Should().ContainSingle().Which.Should().BeOfType<TurnEnded>();
    }

    // No transcript carries the window for Claude Code, so it comes from the capability list — matched
    // against the model the session reports actually running, not the alias the launch asked for.
    [Fact]
    public void The_context_limit_comes_from_the_model_that_is_actually_running()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("model=claude-opus-5");

        tail.Advance().Snapshot!.ContextLimit.Should().Be(1_000_000);
    }

    [Fact]
    public void An_unknown_model_leaves_the_limit_unset_so_no_percentage_is_shown()
    {
        var reader = new FakeReader();
        var tail = Tail(reader);

        reader.Give("model=claude-from-the-future");

        tail.Advance().Snapshot!.ContextLimit.Should().BeNull();
    }

    // Codex reports its own window in the transcript, and an observed number beats a table every time.
    [Fact]
    public void A_limit_the_fold_already_found_is_left_alone()
    {
        var reader = new FakeReader();
        var tail = new TranscriptTail(reader, new FixedLimitNormalizer(), Capabilities, Path);

        reader.Give("anything");

        tail.Advance().Snapshot!.ContextLimit.Should().Be(258_400);
    }

    private static readonly AgentCapabilities Capabilities = new(
        [
            new AgentModel("opus", "Opus 5", ["high"], "high", 1_000_000),
            new AgentModel("haiku", "Haiku 4.5", ["low"], "low", 200_000),
        ],
        "opus",
        new HashSet<PermissionMode>());

    private static TranscriptTail Tail(FakeReader reader)
        => new(reader, new ModelLineNormalizer(), Capabilities, Path);

    private sealed class FakeReader : ITranscriptReader
    {
        private readonly Queue<string> pending = new();

        private long offset;

        public List<long> Offsets { get; } = [];

        public void Give(string line) => pending.Enqueue(line);

        public TranscriptRead Read(string path, long from)
        {
            path.Should().Be(Path);

            Offsets.Add(from);

            if (pending.Count is 0)
                return TranscriptRead.Nothing(from);

            var lines = pending.ToList();

            pending.Clear();

            offset += lines.Sum(line => line.Length);

            return new TranscriptRead(offset, lines);
        }
    }

    // Just enough dialect to prove the tail's own behaviour: `model=<id>` reports a model, `turn`
    // reports an event, anything else reports nothing.
    private sealed class ModelLineNormalizer : ITranscriptNormalizer
    {
        public AgentType Agent => AgentType.ClaudeCode;

        public TranscriptFold Fold(EnrichmentSnapshot snapshot, IReadOnlyList<string> lines)
        {
            var events = new List<AgentEvent>();

            foreach (var line in lines)
            {
                if (line.StartsWith("model=", StringComparison.Ordinal))
                    snapshot = snapshot with { ObservedModel = line["model=".Length..] };

                if (line is "turn")
                    events.Add(new TurnEnded("s", DateTimeOffset.UnixEpoch));
            }

            return new TranscriptFold(snapshot, events);
        }
    }

    private sealed class FixedLimitNormalizer : ITranscriptNormalizer
    {
        public AgentType Agent => AgentType.Codex;

        public TranscriptFold Fold(EnrichmentSnapshot snapshot, IReadOnlyList<string> lines)
            => TranscriptFold.Enrichment(snapshot with { ContextLimit = 258_400, ObservedModel = "opus" });
    }
}
