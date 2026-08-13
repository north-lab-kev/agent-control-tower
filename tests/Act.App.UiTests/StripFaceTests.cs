using System.Globalization;
using Act.Agents.ClaudeCode;
using Act.Agents.Codex;
using Act.App.Cards;
using Act.App.Resources;
using Act.Core.Model;
using Act.Core.Rules;
using Act.Core.Scheduling;
using AwesomeAssertions;

namespace Act.App.UiTests;

// What a glance at the board tells you, which until `StripFace` left the component could not be
// asserted at all. The wording itself is a resource; what is tested here is the *mapping* — which
// badge, which rail colour, whether it blinks, and which chip wins the slot.
public class StripFaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    public StripFaceTests() => CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = new CultureInfo("en");

    [Theory]
    [InlineData(Badge.Running, "r-run")]
    [InlineData(Badge.Compacting, "r-run")]
    [InlineData(Badge.NeedsPermission, "r-wait")]
    [InlineData(Badge.NeedsAnswer, "r-wait")]
    [InlineData(Badge.Error, "r-err")]
    [InlineData(Badge.Killed, "r-err")]
    [InlineData(Badge.ReadyForReview, "r-review")]
    public void The_rail_takes_its_colour_from_the_badge(Badge badge, string expected)
    {
        var card = Card(BoardColumn.Executing);
        card.Badge = badge;

        Face(card).RailClass.Should().Be(expected);
    }

    // With no badge the column supplies the colour instead.
    [Theory]
    [InlineData(BoardColumn.Preparing, "r-plan")]
    [InlineData(BoardColumn.Ready, "r-ready")]
    [InlineData(BoardColumn.Completed, "r-done")]
    public void A_card_with_no_badge_takes_its_colour_from_the_column(BoardColumn column, string expected)
    {
        Face(Card(column)).RailClass.Should().Be(expected);
    }

    // One blink, three colours: something stuck reads differently from something done.
    [Theory]
    [InlineData(Badge.Error, "attn err")]
    [InlineData(Badge.Killed, "attn err")]
    [InlineData(Badge.ReadyForReview, "attn rev")]
    [InlineData(Badge.NeedsPermission, "attn")]
    [InlineData(Badge.NeedsAnswer, "attn")]
    public void A_card_wanting_the_user_asks_for_attention(Badge badge, string expected)
    {
        var card = Card(BoardColumn.YourTurn);
        card.Badge = badge;

        Face(card).AttentionClass.Should().Be(expected);
    }

    [Theory]
    [InlineData(Badge.Running)]
    [InlineData(Badge.Compacting)]
    public void A_card_that_is_working_does_not(Badge badge)
    {
        var card = Card(BoardColumn.Executing);
        card.Badge = badge;

        Face(card).AttentionClass.Should().BeNull();
    }

    // Turning the blink off keeps the colour without the movement.
    [Fact]
    public void The_blink_can_be_stilled_without_losing_the_colour()
    {
        var card = Card(BoardColumn.YourTurn);
        card.Badge = Badge.Error;

        var face = Face(card);

        face.Strip(blink: true, draggable: false, lifted: false, dropEdge: null)
            .Should().Be("strip r-err attn err");

        face.Strip(blink: false, draggable: false, lifted: false, dropEdge: null)
            .Should().Be("strip r-err attn err still");
    }

    [Fact]
    public void A_card_with_nothing_to_say_still_gets_no_stray_classes()
    {
        Face(Card(BoardColumn.Ready)).Strip(blink: true, draggable: true, lifted: false, dropEdge: null)
            .Should().Be("strip r-ready liftable");
    }

    [Fact]
    public void The_drag_state_rides_the_same_class()
    {
        Face(Card(BoardColumn.Ready)).Strip(blink: true, draggable: true, lifted: true, dropEdge: "drop-after")
            .Should().Be("strip r-ready liftable lifted drop-after");
    }

    // A Completed card carries no badge, so the strip supplies `done` where the signal is silent.
    [Fact]
    public void A_signed_off_card_says_done_where_it_has_no_badge()
    {
        var face = Face(Card(BoardColumn.Completed));

        face.BadgeText.Should().Be("done");
        face.BadgeClass.Should().Be("b-done");
        face.FullBadgeClass.Should().Be("badge b-done");
    }

    [Fact]
    public void A_card_before_the_launch_boundary_carries_no_badge_at_all()
    {
        var face = Face(Card(BoardColumn.Ready));

        face.BadgeText.Should().BeNull();
        face.FullBadgeClass.Should().Be("badge");
    }

    // Named the way the task form names it, from the id the transcript actually reports: the strip and
    // the dropdown that chose the model have to read as one vocabulary.
    [Fact]
    public void The_identity_line_names_the_agent_and_the_model_it_is_really_running()
    {
        var card = Card(BoardColumn.Executing);
        card.ObservedModel = "claude-opus-5";

        Face(card).Identity.Should().Be("#1042 · claude · Opus 5");
    }

    // A real id the user can look up beats nothing at all — and beats a guess.
    [Fact]
    public void A_model_no_capability_list_claims_is_named_as_it_came()
    {
        var card = Card(BoardColumn.Executing);
        card.ObservedModel = "claude-instant-1.2";

        Face(card).Identity.Should().Be("#1042 · claude · claude-instant-1.2");
    }

    // Codex reports the id it was asked for rather than a longer one, so the lookup has nothing to
    // shorten — but the strip still shows the display name, because that is the rule for every agent.
    [Fact]
    public void An_id_that_is_already_the_slug_is_still_named_the_way_the_form_names_it()
    {
        var card = Card(BoardColumn.Executing);

        card.AgentType = AgentType.Codex;
        card.ObservedModel = "gpt-5.5";

        StripFace.Of(card, null, Now, null, CodexCapabilities.Current).Identity
            .Should().Be("#1042 · codex · GPT-5.5");
    }

    [Fact]
    public void Before_a_model_is_observed_only_the_agent_is_named()
    {
        Face(Card(BoardColumn.Ready)).Identity.Should().Be("#1042 · claude");
    }

    // Stated beside `running`, never instead of it, and only for a card that has a session to be
    // quiet — see `QuietSession`.
    [Fact]
    public void A_quiet_session_says_how_long_it_has_been_quiet()
    {
        var card = Card(BoardColumn.Executing);
        card.Badge = Badge.Running;
        card.Metrics = new CardMetrics { LastActivityAt = Now.AddMinutes(-40) };

        var face = Face(card);

        face.Quiet.Should().Be("quiet 40m");
        face.BadgeText.Should().Be("running", "the quiet chip states a gap rather than replacing the badge");
    }

    [Fact]
    public void A_long_silence_is_counted_in_hours()
    {
        var card = Card(BoardColumn.Executing);
        card.Metrics = new CardMetrics { LastActivityAt = Now.AddHours(-3) };

        Face(card).Quiet.Should().Be("quiet 3h");
    }

    [Fact]
    public void A_session_that_has_just_spoken_is_not_quiet()
    {
        var card = Card(BoardColumn.Executing);
        card.Metrics = new CardMetrics { LastActivityAt = Now.AddSeconds(-30) };

        Face(card).Quiet.Should().BeNull();
    }

    [Fact]
    public void The_schedule_chip_shows_only_where_a_card_is_waiting_to_start()
    {
        var ready = Card(BoardColumn.Ready);
        ready.Schedule = TaskSchedule.NextWindow;

        Face(ready).Schedule.Should().NotBeNull();

        var running = Card(BoardColumn.Executing);
        running.Schedule = TaskSchedule.NextWindow;

        Face(running).Schedule.Should().BeNull();
    }

    // The hold borrows the badge slot a Ready card leaves empty, and says what it is waiting on.
    [Fact]
    public void A_hold_names_the_card_in_the_way()
    {
        var blocker = Card(BoardColumn.Executing);
        blocker.Number = 7;
        blocker.Title = "Already here";

        var face = Face(Card(BoardColumn.Ready), new ReadyHold(LaunchHold.WorkingDir, Blocker: blocker));

        face.HoldText.Should().Contain("7");
        face.HoldTip.Should().Contain("Already here");
    }

    // The blocker may have no title of its own once title writing is switched off, and "task #7 —" with
    // nothing after it explains nothing. The tip names it the way the board does, from its prompt.
    [Fact]
    public void A_hold_names_an_untitled_card_by_its_prompt()
    {
        var blocker = Card(BoardColumn.Executing);
        blocker.Number = 7;
        blocker.Title = string.Empty;
        blocker.InitialPrompt = "Already working here";

        var face = Face(Card(BoardColumn.Ready), new ReadyHold(LaunchHold.WorkingDir, Blocker: blocker));

        face.HoldTip.Should().Contain("Already working here");
    }

    [Fact]
    public void A_slot_hold_shows_the_figures()
    {
        var face = Face(Card(BoardColumn.Ready), new ReadyHold(LaunchHold.Slot, Used: 5, Cap: 5));

        face.HoldText.Should().Contain("5");
    }

    // A usage hold counts down to the reset, and the wording coarsens with distance.
    [Theory]
    [InlineData(30, "30m")]
    [InlineData(150, "2h30m")]
    [InlineData(3000, "2d2h")]
    public void A_usage_hold_counts_down_to_the_reset(int minutesAway, string expected)
    {
        var face = Face(
            Card(BoardColumn.Ready),
            new ReadyHold(LaunchHold.UsageLimit, Until: Now.AddMinutes(minutesAway)));

        face.HoldText.Should().Contain(expected);
    }

    // A reset that has already passed counts to zero rather than going negative.
    [Fact]
    public void A_reset_already_past_does_not_count_backwards()
    {
        var face = Face(
            Card(BoardColumn.Ready),
            new ReadyHold(LaunchHold.UsageLimit, Until: Now.AddHours(-3)));

        face.HoldText.Should().Contain("0m").And.NotContain("-");
    }

    // `paused` and `agent off` are the user's own doing rather than something in the way, so they stay
    // neutral; the rest carry the tint that says "waiting on something".
    [Theory]
    [InlineData(LaunchHold.Paused, "badge")]
    [InlineData(LaunchHold.AgentDisabled, "badge")]
    [InlineData(LaunchHold.UsageLimit, "badge b-hold")]
    [InlineData(LaunchHold.WorkingDir, "badge b-hold")]
    [InlineData(LaunchHold.Dependency, "badge b-hold")]
    [InlineData(LaunchHold.Slot, "badge b-hold")]
    public void Only_a_real_obstruction_is_tinted(LaunchHold reason, string expected)
    {
        Face(Card(BoardColumn.Ready), new ReadyHold(reason)).HoldClass.Should().Be(expected);
    }

    [Fact]
    public void No_hold_means_no_chip()
    {
        var face = Face(Card(BoardColumn.Ready));

        face.HoldText.Should().BeNull();
        face.HoldTip.Should().BeNull();
    }

    [Fact]
    public void A_card_with_no_metrics_lists_none()
    {
        Face(Card(BoardColumn.Ready)).Metrics.Should().BeEmpty();
    }

    // Only what has happened: a zero is not news, and four zeroes would crowd out the badge.
    [Fact]
    public void Only_the_numbers_that_have_moved_are_listed()
    {
        var card = Card(BoardColumn.Executing);
        card.Metrics = new CardMetrics { TurnCount = 3 };

        Face(card).Metrics.Should().ContainSingle().Which.Should().Contain("3");
    }

    [Fact]
    public void Context_turns_compactions_and_tokens_all_appear_once_they_exist()
    {
        var card = Card(BoardColumn.Executing);
        card.Metrics = new CardMetrics
        {
            ContextUsed = 142_000,
            ContextLimit = 200_000,
            TurnCount = 4,
            Compactions = 1,
            TokensIn = 90_000,
            TokensOut = 10_000,
        };

        Face(card).Metrics.Should().HaveCount(4);
    }

    // A card an agent created is not one the user wrote, and telling them apart at a glance is the whole
    // reason `parentId` is recorded. Both densities carry it — see `FlightStripTests` for the markup.
    [Fact]
    public void A_spawned_card_names_the_task_that_made_it()
    {
        var face = Face(Spawned(), Parent());

        face.Spawned.Should().Be("↳ #1039");
        face.SpawnedTip.Should().Contain("1039").And.Contain("Rework the auth module");
    }

    [Fact]
    public void A_spawned_card_names_an_untitled_parent_by_its_prompt()
    {
        var parent = Parent();

        parent.Title = string.Empty;
        parent.InitialPrompt = "Rework the auth module";

        Face(Spawned(), parent).SpawnedTip.Should().Contain("Rework the auth module");
    }

    [Fact]
    public void A_card_the_user_wrote_carries_no_marker()
    {
        var face = Face(Card(BoardColumn.Ready));

        face.Spawned.Should().BeNull();
        face.SpawnedTip.Should().BeNull();
    }

    // The origin is a fact about how the card came to exist, so it outlives the parent being archived or
    // purged. Dropping the marker when the parent is out of view would quietly relabel an agent's card as
    // something the user wrote.
    [Fact]
    public void A_spawned_card_whose_parent_is_gone_still_says_it_was_spawned()
    {
        var face = Face(Spawned());

        face.Spawned.Should().NotBeNullOrWhiteSpace();
        face.Spawned.Should().NotContain("1039");
        face.SpawnedTip.Should().NotBeNullOrWhiteSpace();
    }

    // Being handed a parent does not make a card spawned — `origin` decides, and a manual card that
    // somehow arrived with one must not start claiming otherwise.
    [Fact]
    public void A_manual_card_is_not_marked_even_if_a_parent_is_supplied()
        => Face(Card(BoardColumn.Ready), Parent()).Spawned.Should().BeNull();

    // The real capability list rather than a stand-in: the mapping from an observed id to a slug is
    // only worth anything if it holds for the models ACT actually ships with.
    private static StripFace Face(Card card, Card? parent)
        => StripFace.Of(card, null, Now, parent, ClaudeCodeCapabilities.Current);

    private static StripFace Face(Card card, ReadyHold? hold = null)
        => StripFace.Of(card, hold, Now, null, ClaudeCodeCapabilities.Current);

    private static Card Spawned()
    {
        var card = Card(BoardColumn.Ready);

        card.Origin = TaskOrigin.Spawned;
        card.SpawnAuthor = SpawnAuthor.Agent;
        card.ParentId = Guid.NewGuid();

        return card;
    }

    // A card with a folder says which one; a card without says so in words. The absolute path of ACT's
    // scratch directory is not the answer — it is noise on every quick question and names an
    // implementation detail rather than anything the user chose.
    [Fact]
    public void A_card_with_a_folder_shows_the_folder()
        => Face(Card(BoardColumn.Ready)).Folder.Should().Be("/dev/act");

    [Fact]
    public void A_card_with_no_folder_says_so_rather_than_showing_a_path()
    {
        var card = Card(BoardColumn.Ready);

        card.NoWorkingDir = true;
        card.WorkingDir = string.Empty;

        Face(card).Folder.Should().Be(Strings.Card_NoWorkingDir);
    }

    // The flag wins over a path left on the card, so a strip cannot show a folder the launch will not use.
    [Fact]
    public void The_flag_decides_what_is_shown_not_the_stored_path()
    {
        var card = Card(BoardColumn.Ready);

        card.NoWorkingDir = true;

        Face(card).Folder.Should().Be(Strings.Card_NoWorkingDir);
    }

    private static Card Parent() => new()
    {
        Number = 1039,
        Title = "Rework the auth module",
        Column = BoardColumn.YourTurn,
        AgentType = AgentType.ClaudeCode,
    };

    private static Card Card(BoardColumn column) => new()
    {
        Number = 1042,
        Title = "Rename the widget",
        Column = column,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
    };
}
