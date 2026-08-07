using Act.App.Sessions;
using Act.Core.Abstractions;
using Act.Core.Events;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// One tail per live session, started by the hooks saying where the file is. What is tested here is
// the pump's own half — which sessions get a loop, what reaches the sink, and the two failures a
// file the CLI still holds open produces.
public class TranscriptPumpTests : ComponentTest
{
    private const string Path = "/transcripts/session.jsonl";

    private readonly FakeTranscriptReader reader = new();

    private readonly FakeTranscriptNormalizer normalizer = new(AgentType.ClaudeCode);

    private readonly RecordingEventSink sink = new();

    [Fact]
    public async Task A_located_transcript_starts_a_tail()
    {
        await ALiveSession();

        await using var pump = Pump();

        pump.Start();

        reader.Reads.Should().Be(0, "nothing is read until the hooks say where the file is");

        Locate();

        await Until(() => reader.Reads >= 1);
    }

    // The whole file on the first read: a restart or a resume must not report a session that has
    // been working for an hour as if it had just started.
    [Fact]
    public async Task The_catch_up_read_publishes_the_snapshot_and_drops_the_history()
    {
        await ALiveSession();

        reader.Lines = ["one", "two"];
        normalizer.Snapshot = new EnrichmentSnapshot { TurnCount = 7, TokensIn = 1200 };
        normalizer.Events = [new TurnEnded("s-1", Now)];

        await using var pump = Pump();

        pump.Start();
        Locate();

        await Until(() => sink.Published.Count >= 1);

        sink.Published.Should().AllBeOfType<SessionEnriched>("the catch-up events are history, not news");

        sink.Published.OfType<SessionEnriched>().First()
            .Snapshot.TurnCount.Should().Be(7);
    }

    [Fact]
    public async Task A_later_read_publishes_the_events_as_well()
    {
        await ALiveSession();

        reader.Lines = ["one"];
        normalizer.Snapshot = new EnrichmentSnapshot { TurnCount = 1 };

        await using var pump = Pump();

        pump.Start();
        Locate();

        await Until(() => sink.Published.Count >= 1);

        normalizer.Snapshot = new EnrichmentSnapshot { TurnCount = 2 };
        normalizer.Events = [new TurnEnded("s-1", Now)];
        reader.Lines = ["two"];

        await Until(() => sink.Published.OfType<TurnEnded>().Any());
    }

    [Fact]
    public async Task A_read_with_nothing_new_publishes_nothing()
    {
        await ALiveSession();

        reader.Lines = [];

        await using var pump = Pump();

        pump.Start();
        Locate();

        await Until(() => reader.Reads >= 2);

        sink.Published.Should().BeEmpty();
    }

    // The transient the loop is built around: the CLI is mid-write and the next tick reads the same
    // offset again.
    [Fact]
    public async Task A_read_that_throws_mid_write_does_not_end_the_tail()
    {
        await ALiveSession();

        reader.Throws = new IOException("the file is being written");

        await using var pump = Pump();

        pump.Start();
        Locate();

        await Until(() => reader.Reads >= 2, "the tail keeps polling through a transient");

        reader.Throws = null;
        reader.Lines = ["one"];
        normalizer.Snapshot = new EnrichmentSnapshot { TurnCount = 1 };

        await Until(() => sink.Published.Count >= 1, "and picks up once the file is readable");
    }

    // Not a transient — a permission or an antivirus lock does not heal on the next tick. The tail
    // keeps polling anyway, because a scanner's hold can still let go.
    [Fact]
    public async Task A_file_that_cannot_be_read_at_all_does_not_end_the_tail_either()
    {
        await ALiveSession();

        reader.Throws = new UnauthorizedAccessException("denied");

        await using var pump = Pump();

        pump.Start();
        Locate();

        await Until(() => reader.Reads >= 2);

        sink.Published.Should().BeEmpty();
    }

    // Not an error: it simply reports what its hooks and its process can say.
    [Fact]
    public async Task An_agent_with_no_transcript_dialect_gets_no_tail()
    {
        await ALiveSession(AgentType.Codex);

        await using var pump = Pump();

        pump.Start();
        Locate();

        await Task.Delay(100);

        reader.Reads.Should().Be(0);
    }

    // The tail needs the card to know which dialect to fold with, so a session whose card is gone —
    // a hook arriving a moment after a delete — has nothing to attach to.
    [Fact]
    public async Task A_session_with_no_card_gets_no_tail()
    {
        var session = new StubSession(Guid.NewGuid());

        Registry.Add(session);

        await using var pump = Pump();

        pump.Start();
        Registry.LocateTranscript(session.TaskId, Path);

        await Task.Delay(100);

        reader.Reads.Should().Be(0);
    }

    // Every hook payload names the transcript, so the registry raises once — which is what stops a
    // second tail being started for the same card.
    [Fact]
    public async Task Locating_the_same_transcript_twice_does_not_start_a_second_tail()
    {
        var card = await ALiveSession();

        reader.Lines = [];

        await using var pump = Pump();

        pump.Start();

        Registry.LocateTranscript(card.Id, Path);
        Registry.LocateTranscript(card.Id, Path);

        await Until(() => reader.Reads >= 2);

        reader.Paths.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task A_pump_that_never_started_never_tails()
    {
        await ALiveSession();

        await using var pump = Pump();

        Locate();

        await Task.Delay(100);

        reader.Reads.Should().Be(0);
    }

    [Fact]
    public async Task Disposing_stops_listening_for_new_transcripts()
    {
        await ALiveSession();

        var pump = Pump();

        pump.Start();

        await pump.DisposeAsync();

        Locate();

        await Task.Delay(100);

        reader.Reads.Should().Be(0);
    }

    // A restart ends the session and registers its replacement inside the same poll gap, and the
    // card is live again before the old tail's next tick. Keyed on the card id the old tail would
    // never exit — two tails on one file, every event published twice — so it is keyed on its own
    // session instance instead.
    [Fact]
    public async Task A_restart_does_not_leave_two_tails_on_one_card()
    {
        var card = await ALiveSession();

        reader.Lines = [];

        await using var pump = Pump();

        pump.Start();
        Locate();

        await Until(() => reader.Reads >= 1);

        (await Launcher.RestartAsync(card, TerminalSize.Default)).Launched.Should().BeTrue();

        Registry.LocateTranscript(card.Id, "/transcripts/after-restart.jsonl");

        await Until(() => reader.Paths.Contains("/transcripts/after-restart.jsonl"));

        await Task.Delay(1200);

        var oldTailReads = reader.Paths.Count(path => path == Path);

        await Task.Delay(1200);

        reader.Paths.Count(path => path == Path).Should().Be(oldTailReads, "the replaced session's tail must stop");
        Registry.IsLive(card.Id).Should().BeTrue();
    }

    [Fact]
    public async Task The_tail_ends_when_the_session_does()
    {
        var card = await ALiveSession();

        reader.Lines = [];

        var pump = Pump();

        pump.Start();
        Locate();

        await Until(() => reader.Reads >= 1);

        await Registry.EndAsync(card.Id);

        await Task.Delay(1200);

        var reads = reader.Reads;

        await Task.Delay(1200);

        reader.Reads.Should().Be(reads);

        await pump.DisposeAsync();
    }

    private TranscriptPump Pump() => new(
        Registry,
        Board,
        sink,
        Services.GetRequiredService<IAgentCapabilityCatalog>(),
        [normalizer],
        reader,
        Clock,
        NullLogger<TranscriptPump>.Instance);

    private void Locate() => Registry.LocateTranscript(live!.Id, Path);

    private Card? live;

    // Through the launcher rather than by handing the registry a stub: `MockAgentSession`'s
    // constructor is internal to `Act.TestSupport`, and a real launch is how a session gets into the
    // registry in production anyway.
    private async Task<Card> ALiveSession(AgentType agent = AgentType.ClaudeCode)
    {
        var card = new Card
        {
            Number = 1,
            Title = "Card 1",
            Column = BoardColumn.Ready,
            AgentType = agent,
            WorkingDir = "/dev/act",
            Schedule = TaskSchedule.Manual,
        };

        await BoardWith(card);

        (await Launcher.LaunchAsync(card, TerminalSize.Default)).Launched.Should().BeTrue();

        live = card;

        return card;
    }

    private static async Task Until(Func<bool> settled, string? because = null)
    {
        for (var waited = 0; waited < 500; waited++)
        {
            if (settled())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException(because ?? "The tail never reached the expected state.");
    }

    // Locked because the restart test briefly has two tails reading at once — the replaced one's
    // last tick beside its successor's first.
    private sealed class FakeTranscriptReader : ITranscriptReader
    {
        private readonly Lock gate = new();

        private readonly List<string> paths = [];

        public IReadOnlyList<string> Lines { get; set; } = [];

        public Exception? Throws { get; set; }

        public int Reads { get; private set; }

        public IReadOnlyList<string> Paths
        {
            get
            {
                lock (gate)
                    return [.. paths];
            }
        }

        public TranscriptRead Read(string path, long offset)
        {
            lock (gate)
            {
                Reads++;
                paths.Add(path);
            }

            if (Throws is { } failure)
                throw failure;

            return new TranscriptRead(offset + Lines.Count, [.. Lines]);
        }
    }

    private sealed class FakeTranscriptNormalizer(AgentType agent) : ITranscriptNormalizer
    {
        public AgentType Agent => agent;

        public EnrichmentSnapshot Snapshot { get; set; } = new();

        public IReadOnlyList<AgentEvent> Events { get; set; } = [];

        public TranscriptFold Fold(EnrichmentSnapshot snapshot, IReadOnlyList<string> lines)
            => new(Snapshot, Events);
    }

    private sealed class StubSession(Guid taskId) : IAgentSession
    {
        public Guid TaskId => taskId;

        public string? SessionId => "s-orphan";

        public IAgentTerminal Terminal => throw new NotSupportedException();

        public IAsyncEnumerable<AgentEvent> Events => AsyncEnumerable.Empty<AgentEvent>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingEventSink : IAgentEventSink
    {
        private readonly List<AgentEvent> published = [];

        private readonly Lock gate = new();

        public IReadOnlyList<AgentEvent> Published
        {
            get
            {
                lock (gate)
                    return [.. published];
            }
        }

        public void Publish(Guid taskId, AgentEvent agentEvent)
        {
            lock (gate)
                published.Add(agentEvent);
        }

        public void Bind(Guid taskId, string sessionId)
        {
        }

        public void LocateTranscript(Guid taskId, string path)
        {
        }
    }
}
