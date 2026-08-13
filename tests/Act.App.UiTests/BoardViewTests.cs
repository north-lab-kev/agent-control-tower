using Act.App.Components.Pages;
using Act.App.Components.Shared;
using Act.Core.Model;
using Act.Core.Scheduling;
using AwesomeAssertions;
using Bunit;
using Radzen;

namespace Act.App.UiTests;

// The board is where every other piece is composed, so what is worth pinning here is the composition:
// five lanes whatever is on them, a strip per card wired to the board's own handlers, and the two
// chips that write straight through to settings. The rules underneath are covered elsewhere —
// `BoardDragTests` owns the gesture, `CardSearch` the query, `StripFace` the strip's face.
public class BoardViewTests : ComponentTest
{
    [Fact]
    public async Task Every_column_gets_a_lane_whether_or_not_anything_is_on_it()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        var cut = Show();

        cut.FindAll("div.col").Should().HaveCount(5);
        cut.FindAll("div.col .colname").Select(name => name.TextContent)
            .Should().Equal("Preparing", "Ready", "Executing", "Your turn", "Completed");
    }

    [Fact]
    public async Task A_lane_head_counts_what_is_on_it()
    {
        await BoardWith(
            Card(1, BoardColumn.Ready),
            Card(2, BoardColumn.Ready),
            Card(3, BoardColumn.Executing));

        Counts(Show()).Should().Equal("0", "2", "1", "0", "0");
    }

    [Fact]
    public async Task An_empty_lane_says_so()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        Show().FindAll("div.col .empty").Should().HaveCount(4);
    }

    [Fact]
    public async Task A_card_becomes_a_strip()
    {
        await BoardWith(Card(1042, BoardColumn.Ready));

        var strip = Show().Find("div.col div.strip");

        strip.GetAttribute("title").Should().Be("Card 1042");
    }

    // The filter narrows what is drawn and nothing else, so the head has to admit to it — a bare `2`
    // beside a lane holding five is the board lying about the store.
    [Fact]
    public async Task Filtering_narrows_the_lanes_and_the_head_says_shown_of_total()
    {
        await BoardWith(
            Card(1, BoardColumn.Ready, "Rename the widget"),
            Card(2, BoardColumn.Ready, "Something else"));

        var cut = Show();

        cut.Find("div.cardfilter input.box").Input("widget");

        cut.FindAll("div.col div.strip").Should().ContainSingle();
        Counts(cut)[1].Should().Be("1/2");
    }

    // The one thing a filter must never be able to hide is the reason the board exists.
    [Fact]
    public async Task A_needy_card_the_query_hid_is_still_announced()
    {
        var needy = Card(1, BoardColumn.YourTurn, "Something else");
        needy.Badge = Badge.NeedsPermission;

        await BoardWith(needy, Card(2, BoardColumn.YourTurn, "Rename the widget"));

        var cut = Show();

        cut.FindAll("div.col .hiddenattn").Should().BeEmpty();

        cut.Find("div.cardfilter input.box").Input("widget");

        cut.FindAll("div.col .hiddenattn").Should().ContainSingle();
        cut.Find("div.col .hiddenattn").TextContent.Should().Contain("1 hidden");
    }

    [Fact]
    public async Task The_archive_is_only_mentioned_when_it_matches()
    {
        var archived = Card(9, BoardColumn.Completed, "Rename the widget");
        archived.DeletedAt = Now.AddDays(-1);

        await BoardWith(archived, Card(1, BoardColumn.Ready, "Something else"));

        var cut = Show();

        cut.FindAll("a.archlink").Should().BeEmpty();

        cut.Find("div.cardfilter input.box").Input("widget");

        cut.Find("a.archlink").GetAttribute("href").Should().Be("/archive?q=widget");
    }

    // Says the mode you are in, not the one you would get: the board in front of you is the answer, and
    // a button naming the other one would disagree with it.
    [Fact]
    public async Task The_density_chip_names_the_board_you_are_looking_at()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        Show().FindAll("div.viewctl button")[1].TextContent.Should().Contain("Detailed");
        Show(BoardDensity.Compact).FindAll("div.viewctl button")[1].TextContent.Should().Contain("Compact");
    }

    [Fact]
    public async Task The_density_chip_writes_through_to_settings()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        Show().FindAll("div.viewctl button")[1].Click();

        Settings.Density.Should().Be(BoardDensity.Compact);
    }

    // The green glyph is what says the queue is live, so a paused board loses it and takes the amber of
    // every other "waiting on you" surface.
    [Fact]
    public async Task The_auto_exec_chip_shows_whether_the_queue_is_live()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        var cut = Show();
        var chip = cut.Find("div.viewctl button");

        chip.ClassList.Should().Contain("live").And.NotContain("paused");

        chip.Click();

        Settings.AutoExecutionPaused.Should().BeTrue();
        cut.Find("div.viewctl button").ClassList.Should().Contain("paused").And.NotContain("live");
    }

    [Fact]
    public async Task The_two_chips_carry_different_explanations_in_each_state()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        var cut = Show();
        var live = cut.Find("div.viewctl button").GetAttribute("title");

        cut.Find("div.viewctl button").Click();

        cut.Find("div.viewctl button").GetAttribute("title").Should().NotBe(live);
    }

    // Past the launch boundary a card *is* its session, so opening one goes to its terminal — the
    // form's fields are not the user's to change any more.
    [Theory]
    [InlineData(BoardColumn.Preparing, "edit")]
    [InlineData(BoardColumn.Ready, "edit")]
    [InlineData(BoardColumn.Executing, "terminal")]
    [InlineData(BoardColumn.YourTurn, "terminal")]
    [InlineData(BoardColumn.Completed, "terminal")]
    public async Task Opening_a_card_lands_on_the_face_its_state_chooses(BoardColumn column, string face)
    {
        var card = Card(1, column);

        await BoardWith(card);

        Show().Find("div.col div.strip").Click();

        Route.Should().Be($"card/{card.Id}/{face}");
    }

    [Fact]
    public async Task A_new_task_is_asked_for_where_new_tasks_land()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        var cut = Show();

        cut.FindAll("div.col")[0].QuerySelector("button.newtaskbtn").Should().NotBeNull();
        cut.FindAll("div.col").Skip(1).SelectMany(col => col.QuerySelectorAll("button.newtaskbtn"))
            .Should().BeEmpty();

        cut.Find("button.newtaskbtn").Click();

        Route.Should().Be("card/new");
    }

    // A split button only once there is something else to start from: the button itself is the default
    // template, so with nothing but that the caret would drop a menu repeating the button it hangs off.
    [Fact]
    public async Task With_only_the_default_template_there_is_nothing_to_drop_down()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        var head = Show().FindAll("div.col")[0];

        head.QuerySelectorAll("div.rz-splitbutton").Should().BeEmpty();
        head.QuerySelectorAll("button.newtaskbtn").Should().ContainSingle();
    }

    [Fact]
    public async Task The_default_template_is_never_offered_in_the_menu_it_already_is()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        Settings.SaveTemplate(new TaskTemplate { Name = "Nightly sweep" });

        var head = Show().FindAll("div.col")[0];

        head.QuerySelectorAll("div.rz-splitbutton.newtaskbtn").Should().ContainSingle();
        head.QuerySelectorAll("ul.rz-menu-list li").Should().ContainSingle()
            .Which.TextContent.Should().Be("Nightly sweep");
    }

    // The pickable list is cached — `settings.Templates` copies and sorts on every call — so the one
    // thing the cache must prove is that it empties when the templates change under an open board.
    [Fact]
    public async Task A_template_saved_while_the_board_is_open_appears_without_a_reload()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        var cut = Show();

        cut.FindAll("div.rz-splitbutton").Should().BeEmpty();

        await cut.InvokeAsync(() => Settings.SaveTemplate(new TaskTemplate { Name = "Nightly sweep" }));

        cut.WaitForAssertion(() =>
            cut.FindAll("div.col")[0].QuerySelectorAll("div.rz-splitbutton.newtaskbtn")
                .Should().ContainSingle());
    }

    [Fact]
    public async Task Starting_from_a_named_template_carries_it_into_the_form()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        Settings.SaveTemplate(new TaskTemplate { Name = "Nightly sweep" });

        var id = Settings.Templates.Single(template => !template.IsDefault).Id;
        var cut = Show();

        cut.Find("ul.rz-menu-list li").Click();

        Route.Should().Be($"card/new?template={id}");
    }

    // Preparing is promoted first rather than launched where it stands: the launcher refuses Preparing
    // outright, so the button does the drag for you.
    [Fact]
    public async Task Launching_a_draft_promotes_it_before_it_starts()
    {
        var card = Card(1, BoardColumn.Preparing);

        await BoardWith(card);

        Show().Find("div.col button.launch").Click();

        Claude.Launches.Should().ContainSingle();
        Board.Card(card.Id)!.Column.Should().Be(BoardColumn.Executing);
    }

    // Reported through the notification host rather than onto the strip: a launch failure is usually a
    // path or a command line, which a 15rem strip cannot show, and it is transient news rather than part
    // of the card's description.
    [Fact]
    public async Task A_launch_that_never_started_is_reported_as_an_error()
    {
        Claude.Fails = new InvalidOperationException("claude is not on PATH");

        await BoardWith(Card(1, BoardColumn.Ready));

        Show().Find("div.col button.launch").Click();

        var message = Notifications.Messages.Should().ContainSingle().Subject;

        message.Severity.Should().Be(NotificationSeverity.Error);
        message.Detail.Should().Be("claude is not on PATH");
    }

    // A refused launch is not a failure: the folder frees up on its own and the card is still sitting in
    // Ready, so it is amber rather than red.
    [Fact]
    public async Task A_launch_that_is_only_waiting_is_reported_as_a_warning()
    {
        var holder = Card(7, BoardColumn.Executing, "Already here");
        holder.Badge = Badge.Running;

        await BoardWith(holder, Card(1, BoardColumn.Ready));

        Show().Find("div.col button.launch").Click();

        var message = Notifications.Messages.Should().ContainSingle().Subject;

        message.Severity.Should().Be(NotificationSeverity.Warning);
        message.Detail.Should().Contain("7").And.Contain("Already here");
    }

    // Retry is the board's, not the session view's: opening a failed card's terminal already resumes
    // it, so by then the process is live and typing into it is the user's job.
    [Fact]
    public async Task Retry_shows_only_where_the_launcher_says_a_card_can_be_retried()
    {
        var failed = Card(1, BoardColumn.YourTurn);
        failed.Badge = Badge.Error;
        failed.SessionId = "c0ffee00-0000-4a2c-9f4d-2f0a3f7c1e22";

        await BoardWith(failed, Card(2, BoardColumn.Ready));

        var cut = Show();

        Launcher.CanRetry(failed).Should().BeTrue();
        cut.FindAll("div.col button.launch").Should().HaveCount(2, "one is the retry, one is the launch");

        cut.FindAll("div.col")[3].QuerySelectorAll("button.launch").Should().ContainSingle();
    }

    // Read from the runner rather than computed per render, so the chip a Ready card shows and the
    // decision the runner acts on are literally the same evaluation.
    [Fact]
    public async Task A_hold_the_queue_decided_reaches_the_strip_that_owns_it()
    {
        Settings.SetAutoExecutionPaused(true);

        var queued = Card(1, BoardColumn.Ready);
        queued.Schedule = TaskSchedule.Now;

        await BoardWith(queued);

        Queue.Evaluate();

        var cut = Show();

        Queue.Latest.Holds.Should().ContainSingle().Which.Value.Reason.Should().Be(LaunchHold.Paused);
        cut.Find("div.col div.strip").TextContent.Should().Contain("paused");
    }

    // Three subscriptions, three sources that move the board without anything on screen being touched:
    // an event pump, the queue's own pass, and a setting changed on another page.
    [Fact]
    public async Task The_board_follows_a_card_that_moved_underneath_it()
    {
        var card = Card(1, BoardColumn.Ready);

        await BoardWith(card);

        var cut = Show();

        Counts(cut)[1].Should().Be("1");

        card.Column = BoardColumn.Executing;
        await Board.UpdateAsync(card);

        cut.WaitForAssertion(() => Counts(cut)[2].Should().Be("1"));
    }

    [Fact]
    public async Task The_board_follows_a_setting_changed_somewhere_else()
    {
        await BoardWith(Card(1, BoardColumn.Ready));

        var cut = Show();

        Settings.SetAutoExecutionPaused(true);

        cut.WaitForAssertion(() => cut.Find("div.viewctl button").ClassList.Should().Contain("paused"));
    }

    private static IReadOnlyList<string> Counts(IRenderedComponent<BoardView> cut)
        => [.. cut.FindAll("div.col .colcount").Select(badge => badge.TextContent)];

    // Deleting from the strip, which is the whole point of the button: no trip to the task page. A card
    // that never launched goes straight away — asking would be ceremony on the gesture used to tidy up.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Completed)]
    public async Task A_card_with_no_live_agent_is_deleted_without_a_question(BoardColumn column)
    {
        await BoardWith(Card(1, column));

        var cut = Show();

        cut.Find("button.del").Click();

        // Soft: the card leaves the board and lands in the archive, which is what makes deleting from a
        // strip a safe gesture in the first place.
        cut.WaitForAssertion(() => Board.Archived.Should().ContainSingle());

        Board.In(column).Should().BeEmpty();
        DialogsOpened.Should().BeEmpty("nothing to ask about a card that never launched");
    }

    // Executing and Your turn both end a live agent, and the archive cannot put a process back — so the
    // board stops and names the card before it does.
    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public async Task A_card_with_a_live_agent_asks_first_and_stays_until_answered(BoardColumn column)
    {
        await BoardWith(Card(1, column, "Rename the widget"));

        var cut = Show();

        cut.Find("button.del").Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());
        Board.In(column).Should().ContainSingle("nothing goes until the question is answered");
    }

    [Fact]
    public async Task Confirming_deletes_the_card()
    {
        await BoardWith(Card(1, BoardColumn.Executing));

        var cut = Show();

        cut.Find("button.del").Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, true);

        cut.WaitForAssertion(() => Board.Archived.Should().ContainSingle());

        Board.In(BoardColumn.Executing).Should().BeEmpty();
    }

    // Both ways of saying no: the Cancel button answers false, and dismissing with the X or the overlay
    // answers null. A null read as anything but "keep it" would delete a card the user backed out of.
    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Declining_keeps_the_card_and_its_agent(bool? answer)
    {
        await BoardWith(Card(1, BoardColumn.Executing));

        var cut = Show();

        cut.Find("button.del").Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, answer);

        cut.WaitForAssertion(() => Board.In(BoardColumn.Executing).Should().ContainSingle());
    }

    // The same dialog the task page opens, and for the same reason: what happens to the follow-ups is a
    // genuine choice, not an "are you sure". A card whose children silently outlived it is the orphan
    // the board exists to make hard to create.
    [Fact]
    public async Task A_card_with_follow_ups_is_asked_about_them_rather_than_orphaning_them()
    {
        var parent = Card(1, BoardColumn.Ready);
        var child = Card(2, BoardColumn.Ready);

        child.ParentId = parent.Id;
        parent.Children.Add(child.Id);

        await BoardWith(parent, child);

        var cut = Show();

        // By title rather than by index: both cards sit in the same lane, and `CardOrder` — not the
        // order they were handed to the board — decides which strip is drawn first.
        cut.FindAll("div.col div.strip")
            .Single(strip => strip.GetAttribute("title") == "Card 1")
            .QuerySelector("button.del")!
            .Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle()
            .Which.Dialog.Should().Be<ArchiveFollowUpsDialog>());

        Board.In(BoardColumn.Ready).Should().HaveCount(2, "nothing goes until the choice is made");

        await AnswerDialog(cut, FollowUpChoice.KeepFollowUps);

        cut.WaitForAssertion(() => Board.In(BoardColumn.Ready).Should().ContainSingle().Which.Number.Should().Be(2));
    }

    private IRenderedComponent<BoardView> Show(BoardDensity density = BoardDensity.Detailed)
        => Render<BoardView>(p => p
            .AddCascadingValue("Density", density)
            .AddCascadingValue("BlinkYourTurn", true));

    private static Card Card(int number, BoardColumn column, string? title = null) => new()
    {
        Number = number,
        Title = title ?? $"Card {number}",
        Column = column,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
        Schedule = TaskSchedule.Manual,
    };
}
