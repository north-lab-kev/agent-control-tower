using Act.Core.Events;
using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class RulesEngineTests
{
    private const string SessionId = "abc";

    private static readonly DateTimeOffset At = new(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);

    // The spec's transition table, one row per case, asserted against the columns and badges it
    // names. If the table and this disagree, one of them is a bug.
    [Theory]
    [InlineData(BoardColumn.Executing, BoardColumn.Executing, Badge.Running)]
    [InlineData(BoardColumn.YourTurn, BoardColumn.Executing, Badge.Running)]
    public void Activity_puts_a_governed_card_back_to_running(
        BoardColumn from,
        BoardColumn expected,
        Badge badge)
    {
        var move = RulesEngine.Decide(CardIn(from), new ActivityObserved(SessionId, At));

        move.Should().NotBeNull();
        move!.Column.Should().Be(expected);
        move.Badge.Should().Be(badge);
    }

    [Fact]
    public void Compacting_shows_on_the_card_and_then_clears()
    {
        RulesEngine.Decide(CardIn(BoardColumn.Executing), new CompactingStarted(SessionId, At))!
            .Badge.Should().Be(Badge.Compacting);

        RulesEngine.Decide(CardIn(BoardColumn.Executing, Badge.Compacting), new CompactingFinished(SessionId, At))!
            .Badge.Should().Be(Badge.Running);
    }

    [Fact]
    public void A_permission_request_blocks_the_card_and_carries_a_read_only_summary()
    {
        var move = RulesEngine.Decide(
            CardIn(BoardColumn.Executing),
            new PermissionRequested(SessionId, At, "req-1", "run a shell command"));

        move!.Column.Should().Be(BoardColumn.YourTurn);
        move.Badge.Should().Be(Badge.NeedsPermission);
        move.Message.Should().Be("run a shell command");
    }

    [Fact]
    public void A_question_blocks_the_card_and_carries_the_question()
    {
        var move = RulesEngine.Decide(
            CardIn(BoardColumn.Executing),
            new QuestionAsked(SessionId, At, "req-1", "which database?"));

        move!.Column.Should().Be(BoardColumn.YourTurn);
        move.Badge.Should().Be(Badge.NeedsAnswer);
        move.Message.Should().Be("which database?");
    }

    // Claude Code prompts for its question tool the way it prompts for any other, so the generic
    // permission notice arrives a beat after the question and describes the same block. Left alone it
    // would replace `needs answer` with `needs permission` and nothing to approve.
    [Fact]
    public void A_permission_request_does_not_overwrite_an_unanswered_question()
        => RulesEngine.Decide(
                CardIn(BoardColumn.YourTurn, Badge.NeedsAnswer),
                new PermissionRequested(SessionId, At, "req-1", "Claude needs your permission"))
            .Should().BeNull();

    // Everything else the card may be waiting on says less than a permission prompt does, so the
    // prompt still wins — including the review badge, which is where the idle-nudge bug used to land.
    [Theory]
    [InlineData(BoardColumn.Executing, Badge.Running)]
    [InlineData(BoardColumn.Executing, Badge.Stale)]
    [InlineData(BoardColumn.YourTurn, Badge.ReadyForReview)]
    [InlineData(BoardColumn.YourTurn, Badge.NeedsPermission)]
    public void A_permission_request_still_blocks_every_other_card(BoardColumn column, Badge badge)
        => RulesEngine.Decide(
                CardIn(column, badge),
                new PermissionRequested(SessionId, At, "req-1", "run a shell command"))!
            .Badge.Should().Be(Badge.NeedsPermission);

    // The directory-trust prompt blocks before the session exists, so no hook reports it and the
    // card would otherwise sit in Executing claiming to run while the CLI waits on a keypress.
    [Fact]
    public void A_silent_launch_is_reported_as_the_startup_prompt()
    {
        var move = RulesEngine.Decide(CardIn(BoardColumn.Executing), new StartupPromptWaiting(SessionId, At));

        move!.Column.Should().Be(BoardColumn.YourTurn);
        move.Badge.Should().Be(Badge.NeedsPermission);
        move.Reason.Should().Be(TransitionReason.StartupPrompt);
    }

    // Silence says less than anything the agent itself reported, and a restored card is sitting in
    // Your turn precisely because the rules put it there. Neither may be overwritten by a guess.
    [Theory]
    [InlineData(Badge.ReadyForReview)]
    [InlineData(Badge.NeedsAnswer)]
    [InlineData(Badge.Killed)]
    public void The_startup_prompt_never_disturbs_a_card_already_in_your_turn(Badge badge)
        => RulesEngine.Decide(CardIn(BoardColumn.YourTurn, badge), new StartupPromptWaiting(SessionId, At))
            .Should().BeNull();

    // Recovery needs no rule of its own: the answer starts the session, and the session's first
    // hook is activity.
    [Fact]
    public void Answering_the_startup_prompt_comes_back_as_activity()
        => RulesEngine.Decide(
                CardIn(BoardColumn.YourTurn, Badge.NeedsPermission),
                new ActivityObserved(SessionId, At))!
            .Column.Should().Be(BoardColumn.Executing);

    // The recovery half of the pair: the tool finishing is the user having answered, and it has to
    // move the card back out even though a permission request no longer can.
    [Fact]
    public void Activity_still_recovers_a_card_holding_a_question()
        => RulesEngine.Decide(
                CardIn(BoardColumn.YourTurn, Badge.NeedsAnswer),
                new ActivityObserved(SessionId, At, "AskUserQuestion"))!
            .Badge.Should().Be(Badge.Running);

    // There is nothing left for a turn end to route on: no signal separates finished from blocked,
    // so it always offers the work up for review.
    [Fact]
    public void A_turn_ending_lands_on_review()
    {
        var move = RulesEngine.Decide(CardIn(BoardColumn.Executing), new TurnEnded(SessionId, At));

        move!.Column.Should().Be(BoardColumn.YourTurn);
        move.Badge.Should().Be(Badge.ReadyForReview);
        move.Reason.Should().Be(TransitionReason.TurnEnded);
    }

    // A turn that ends while the card is blocked ends for a reason — the prompt was denied and the
    // agent stopped — so it must still be offered for review rather than left waiting on a prompt
    // nobody is answering any more.
    [Theory]
    [InlineData(Badge.NeedsAnswer)]
    [InlineData(Badge.NeedsPermission)]
    public void A_turn_ending_supersedes_a_block_that_is_over(Badge badge)
        => RulesEngine.Decide(CardIn(BoardColumn.YourTurn, badge), new TurnEnded(SessionId, At))!
            .Badge.Should().Be(Badge.ReadyForReview);

    [Fact]
    public void A_non_zero_exit_is_an_error_the_user_can_retry()
    {
        var move = RulesEngine.Decide(CardIn(BoardColumn.Executing), new ProcessExited(SessionId, At, 1));

        move!.Column.Should().Be(BoardColumn.YourTurn);
        move.Badge.Should().Be(Badge.Error);
        move.Reason.Should().Be(TransitionReason.AgentExited);

        // The code itself is data, not wording: it is stored verbatim and dropped into whatever
        // sentence the UI's current language uses.
        move.Detail.Should().Be("1");
    }

    // A clean exit is not a card state: the turn events already said where the work stands, and the
    // session merely being over must not overwrite that with something less specific.
    [Fact]
    public void A_clean_exit_moves_nothing()
        => RulesEngine.Decide(CardIn(BoardColumn.YourTurn, Badge.ReadyForReview), new ProcessExited(SessionId, At, 0))
            .Should().BeNull();

    [Fact]
    public void A_kill_lands_the_card_rather_than_leaving_it_running()
    {
        var move = RulesEngine.Decide(CardIn(BoardColumn.Executing), new SessionKilled(SessionId, At));

        move!.Column.Should().Be(BoardColumn.YourTurn);
        move.Badge.Should().Be(Badge.Killed);
    }

    // Stale is a badge and not a move — the TUI is still alive and the work may still be fine.
    [Fact]
    public void Going_quiet_marks_the_card_without_moving_it()
    {
        var move = RulesEngine.Decide(CardIn(BoardColumn.Executing), new NoActivityElapsed(SessionId, At, TimeSpan.FromHours(2)));

        move!.Column.Should().Be(BoardColumn.Executing);
        move.Badge.Should().Be(Badge.Stale);
    }

    // The launch boundary, from the other side: observation must never drag a card back across it.
    // A dying session reporting one last thing cannot un-complete finished work or start unstarted
    // work, and this is the assertion that says so for every event the engine knows.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Completed)]
    public void Cards_outside_the_machine_region_are_never_moved_by_an_event(BoardColumn column)
    {
        foreach (var observed in EveryEvent())
            RulesEngine.Decide(CardIn(column), observed).Should().BeNull($"{column} is not the machine's to move");
    }

    [Theory]
    [InlineData(BoardColumn.Preparing, false)]
    [InlineData(BoardColumn.Ready, false)]
    [InlineData(BoardColumn.Executing, true)]
    [InlineData(BoardColumn.YourTurn, true)]
    [InlineData(BoardColumn.Completed, false)]
    public void The_governed_region_is_exactly_the_machine_columns(BoardColumn column, bool governed)
        => RulesEngine.Governs(column).Should().Be(governed);

    // Ready → Executing is ACT spawning a process, and Your turn → Completed is the user's call.
    // Neither is a rule, so no event may produce them.
    [Fact]
    public void No_event_ever_produces_a_launch_or_a_completion()
    {
        foreach (var column in Enum.GetValues<BoardColumn>())
        {
            foreach (var observed in EveryEvent())
            {
                var move = RulesEngine.Decide(CardIn(column), observed);

                move?.Column.Should().NotBe(BoardColumn.Completed);
                move?.Column.Should().NotBe(BoardColumn.Ready);
                move?.Column.Should().NotBe(BoardColumn.Preparing);
            }
        }
    }

    [Fact]
    public void An_event_the_engine_has_no_rule_for_moves_nothing()
    {
        RulesEngine.Decide(CardIn(BoardColumn.Executing), new SessionEnded(SessionId, At)).Should().BeNull();
        RulesEngine.Decide(
                CardIn(BoardColumn.Executing),
                new SessionStarted(SessionId, At, "t.jsonl", "C:/repo"))
            .Should().BeNull();
        RulesEngine.Decide(
                CardIn(BoardColumn.Executing),
                new SessionEnriched(SessionId, At, new EnrichmentSnapshot()))
            .Should().BeNull();
    }

    // The pump persists on a move, so "nothing changed" has to be recognisable or a running session
    // would rewrite and re-render the board on every tool call it makes.
    [Fact]
    public void A_move_that_matches_the_card_changes_nothing()
    {
        var card = CardIn(BoardColumn.Executing, Badge.Running);

        var move = RulesEngine.Decide(card, new ActivityObserved(SessionId, At))!;

        move.ChangesAnything(card).Should().BeFalse();
        move.ChangesAnything(CardIn(BoardColumn.YourTurn, Badge.NeedsAnswer)).Should().BeTrue();
    }

    // The rule the whole `TransitionReason` design exists for: a transition is stored, so nothing
    // here may produce display text. A localized sentence written into the store would be frozen in
    // whatever language was active at the time, and would still be in it after the user switches.
    [Fact]
    public void No_move_ever_carries_display_text()
    {
        foreach (var column in Enum.GetValues<BoardColumn>())
        {
            foreach (var observed in EveryEvent())
            {
                if (RulesEngine.Decide(CardIn(column), observed) is not { } move)
                    continue;

                // A detail is verbatim data — an exit code, a duration — never a sentence.
                move.Detail.Should().NotContain(" ");
            }
        }
    }

    private static IEnumerable<AgentEvent> EveryEvent() =>
    [
        new ActivityObserved(SessionId, At),
        new ActivityObserved(SessionId, At, "Bash"),
        new CompactingStarted(SessionId, At),
        new CompactingFinished(SessionId, At),
        new PermissionRequested(SessionId, At, "req", "summary"),
        new QuestionAsked(SessionId, At, "req", "question"),
        new TurnEnded(SessionId, At),
        new ProcessExited(SessionId, At, 0),
        new ProcessExited(SessionId, At, 3),
        new SessionKilled(SessionId, At),
        new SessionEnded(SessionId, At),
        new SessionStarted(SessionId, At, "t.jsonl", "C:/repo"),
        new SessionEnriched(SessionId, At, new EnrichmentSnapshot()),
        new NoActivityElapsed(SessionId, At, TimeSpan.FromHours(2)),
        new FollowUpsWritten(SessionId, At, ["001.json"]),
        new StartupPromptWaiting(SessionId, At),
    ];

    private static Card CardIn(BoardColumn column, Badge? badge = null)
        => new() { Column = column, Badge = badge, SessionId = SessionId };
}
