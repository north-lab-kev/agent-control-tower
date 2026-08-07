using Act.App.Sessions;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Events;
using Act.Core.Model;
using Act.TestSupport;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Act.App.UiTests;

// The one piece that turns "task 1234 observed this" into "this session's stream carries this". The
// hook endpoint knows only a task id, because that is all a token can prove.
public class SessionEventSinkTests : ComponentTest
{
    [Fact]
    public async Task An_event_for_a_live_session_reaches_its_stream()
    {
        var (card, session) = await ALiveSession();

        Sink().Publish(card.Id, new TurnEnded("s-1", Now));

        (await Next(session)).Should().BeOfType<TurnEnded>();
    }

    // Hooks can outlive a killed process by a moment, and there is nothing to tell about a session
    // that is gone.
    [Fact]
    public void An_event_for_a_task_with_no_live_session_is_dropped()
    {
        var publish = () => Sink().Publish(Guid.NewGuid(), new TurnEnded("s-1", Now));

        publish.Should().NotThrow();
    }

    [Fact]
    public async Task An_event_for_a_session_that_is_not_pty_hosted_is_dropped()
    {
        var card = await ACard();

        Registry.Add(new StubSession(card.Id));

        var publish = () => Sink().Publish(card.Id, new TurnEnded("s-1", Now));

        publish.Should().NotThrow();
    }

    // A session object dies with its process, so an agent whose id ACT cannot pre-mint has nowhere
    // else for the binding to survive — without it the rail reads "reported at session start" for
    // the whole session and a resume has no id to resume.
    [Fact]
    public async Task Binding_names_the_session_and_writes_it_to_the_card()
    {
        var (card, session) = await ALiveSession(sessionId: null);

        Sink().Bind(card.Id, "codex-77");

        session.SessionId.Should().Be("codex-77");

        await Until(() => card.SessionId == "codex-77");

        Cards.Updated.Should().Contain(card);
    }

    // Every payload carries the id and several arrive a second, so only the first one has anything
    // to write.
    [Fact]
    public async Task Binding_the_id_a_session_already_has_writes_nothing()
    {
        var (card, _) = await ALiveSession(sessionId: "s-1");

        card.SessionId = "s-1";
        Cards.Updated.Clear();

        Sink().Bind(card.Id, "s-1");

        await Task.Delay(50);

        Cards.Updated.Should().BeEmpty();
    }

    [Fact]
    public void Binding_for_a_task_with_no_live_session_is_dropped()
    {
        var bind = () => Sink().Bind(Guid.NewGuid(), "codex-77");

        bind.Should().NotThrow();
    }

    [Fact]
    public async Task Binding_a_session_whose_card_is_gone_still_names_the_session()
    {
        var (card, session) = await ALiveSession(sessionId: null);

        await Board.DeleteAsync(card, includeChildren: false);

        var bind = () => Sink().Bind(card.Id, "codex-77");

        bind.Should().NotThrow();
        session.SessionId.Should().Be("codex-77");
    }

    [Fact]
    public async Task Locating_a_transcript_is_handed_straight_to_the_registry()
    {
        var (card, _) = await ALiveSession();

        var located = new List<string>();
        Registry.TranscriptLocated += (_, path) => located.Add(path);

        Sink().LocateTranscript(card.Id, "/transcripts/session.jsonl");

        located.Should().Equal("/transcripts/session.jsonl");
    }

    private SessionEventSink Sink()
        => new(Registry, Board, NullLogger<SessionEventSink>.Instance);

    private async Task<Card> ACard()
    {
        var card = new Card
        {
            Number = 1,
            Title = "Card 1",
            Column = BoardColumn.Executing,
            AgentType = AgentType.Codex,
            WorkingDir = "/dev/act",
            Schedule = TaskSchedule.Manual,
        };

        await BoardWith(card);

        return card;
    }

    // A real `PtyAgentSession`, because that is the type the sink tests for — with no startup grace,
    // so nothing writes a `StartupPromptWaiting` into the stream a test is reading.
    private async Task<(Card Card, PtyAgentSession Session)> ALiveSession(string? sessionId = "s-1")
    {
        var card = await ACard();

        var session = new PtyAgentSession(card.Id, sessionId, new StubPtyProcess(), Clock, TimeSpan.Zero);

        Registry.Add(session);

        return (card, session);
    }

    private static async Task<AgentEvent> Next(IAgentSession session)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var observed in session.Events.WithCancellation(timeout.Token))
            return observed;

        throw new TimeoutException("The session's stream carried nothing.");
    }

    private static async Task Until(Func<bool> settled)
    {
        for (var waited = 0; waited < 200; waited++)
        {
            if (settled())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("The sink never reached the expected state.");
    }

    private sealed class StubSession(Guid taskId) : IAgentSession
    {
        public Guid TaskId => taskId;

        public string? SessionId => "s-not-pty";

        public IAgentTerminal Terminal => throw new NotSupportedException();

        public IAsyncEnumerable<AgentEvent> Events => AsyncEnumerable.Empty<AgentEvent>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
