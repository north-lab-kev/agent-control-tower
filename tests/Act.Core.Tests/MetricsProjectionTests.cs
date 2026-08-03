using Act.Core.Events;
using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class MetricsProjectionTests
{
    private const string SessionId = "abc";

    private static readonly DateTimeOffset At = new(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_card_with_no_metrics_yet_gets_them()
    {
        var card = new Card();

        MetricsProjection.Apply(card, new ActivityObserved(SessionId, At)).Should().BeTrue();

        card.Metrics!.LastActivityAt.Should().Be(At);
    }

    // The stamp on an event is the *source's*, not ACT's: a transcript line carries its own, and one
    // ACT cannot read a timestamp out of falls back to a sentinel. Assigning it would let "last
    // activity" walk backwards — and a stamp in 1970 makes a working card claim it has been silent
    // for decades, which is exactly what the quiet chip reads.
    [Fact]
    public void An_event_stamped_in_the_past_does_not_drag_the_activity_stamp_back()
    {
        var card = new Card();

        MetricsProjection.Apply(card, new ActivityObserved(SessionId, At));
        MetricsProjection.Apply(card, new TurnFailed(SessionId, DateTimeOffset.UnixEpoch, "no timestamp"));

        card.Metrics!.LastActivityAt.Should().Be(At);

        // Still a turn, still counted — only the stamp is refused.
        card.Metrics.TurnCount.Should().Be(1);
    }

    // What the quiet chip is measured from, so every event that means "the session is alive" has to
    // move it — an unmoved stamp is a card that claims to have gone silent while it was working.
    [Fact]
    public void Every_sign_of_life_refreshes_the_activity_stamp()
    {
        var later = At.AddMinutes(5);

        foreach (var observed in new AgentEvent[]
        {
            new ActivityObserved(SessionId, later),
            new TurnEnded(SessionId, later),
            new CompactingStarted(SessionId, later),
            new CompactingFinished(SessionId, later),
            new PermissionRequested(SessionId, later, "req", "summary"),
            new QuestionAsked(SessionId, later, "req", "question"),
            new SessionStarted(SessionId, later, "t.jsonl", "C:/repo"),
        })
        {
            var card = new Card { Metrics = new CardMetrics { LastActivityAt = At } };

            MetricsProjection.Apply(card, observed);

            card.Metrics!.LastActivityAt.Should().Be(later, $"{observed.GetType().Name} is a sign of life");
        }
    }

    // `UserPromptSubmit` arrives as activity with no tool name, and counting it would inflate the
    // number the flight strip shows.
    [Fact]
    public void Only_a_named_tool_counts_as_a_tool_call()
    {
        var card = new Card();

        MetricsProjection.Apply(card, new ActivityObserved(SessionId, At));
        MetricsProjection.Apply(card, new ActivityObserved(SessionId, At, "Bash"));
        MetricsProjection.Apply(card, new ActivityObserved(SessionId, At, "Edit"));

        card.Metrics!.ToolCalls.Should().Be(2);
    }

    [Fact]
    public void Turns_and_compactions_accumulate()
    {
        var card = new Card();

        MetricsProjection.Apply(card, new TurnEnded(SessionId, At));

        // A failed turn was still attempted and is still over, so hiding it from the count would make
        // a card that failed twice look untouched.
        MetricsProjection.Apply(card, new TurnFailed(SessionId, At, "api error"));
        MetricsProjection.Apply(card, new CompactingStarted(SessionId, At));

        card.Metrics!.TurnCount.Should().Be(2);
        card.Metrics.Compactions.Should().Be(1);
    }

    [Fact]
    public void Enrichment_fills_what_it_carries()
    {
        var card = new Card();

        MetricsProjection.Apply(card, new SessionEnriched(SessionId, At, new EnrichmentSnapshot
        {
            TokensIn = 1_200,
            TokensOut = 300,
            ContextUsed = 142_000,
            ContextLimit = 200_000,
            ObservedModel = "sonnet-5",
        }));

        card.Metrics!.TokensTotal.Should().Be(1_500);
        card.Metrics.ContextPercent.Should().Be(71);
        card.ObservedModel.Should().Be("sonnet-5");
    }

    // Null means "no news", not zero: a transcript line may carry only a token count, and a naive
    // merge would blank the context reading every time one arrived.
    [Fact]
    public void An_absent_field_leaves_what_was_already_known_alone()
    {
        var card = new Card
        {
            ObservedModel = "sonnet-5",
            Metrics = new CardMetrics { ContextUsed = 142_000, ContextLimit = 200_000, TokensIn = 900 },
        };

        MetricsProjection.Apply(card, new SessionEnriched(SessionId, At, new EnrichmentSnapshot
        {
            TokensOut = 50,
        }));

        card.Metrics!.TokensIn.Should().Be(900);
        card.Metrics.ContextUsed.Should().Be(142_000);
        card.ObservedModel.Should().Be("sonnet-5");
    }

    [Fact]
    public void An_event_with_no_numbers_in_it_touches_nothing()
    {
        var card = new Card();

        MetricsProjection.Apply(card, new SessionEnded(SessionId, At)).Should().BeFalse();
        MetricsProjection.Apply(card, new FollowUpsWritten(SessionId, At, ["001.json"])).Should().BeFalse();
    }

    // Metrics never route anything — that is the rules engine's job — so applying any event must
    // leave the card exactly where it was.
    [Fact]
    public void The_projection_never_moves_a_card()
    {
        foreach (var column in Enum.GetValues<BoardColumn>())
        {
            var card = new Card { Column = column, Badge = Badge.Running };

            MetricsProjection.Apply(card, new TurnEnded(SessionId, At));

            card.Column.Should().Be(column);
            card.Badge.Should().Be(Badge.Running);
        }
    }
}
