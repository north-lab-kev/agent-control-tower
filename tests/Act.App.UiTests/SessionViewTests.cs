using Act.App.Components.Pages;
using Act.App.Resources;
using Act.Core.Abstractions;
using Act.Core.Agents;
using Act.Core.Model;
using Act.Core.Spawning;
using Act.TestSupport;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;

namespace Act.App.UiTests;

// The terminal attaches to a session the registry already holds rather than owning one, so navigating away
// and back re-attaches instead of starting anything. The xterm itself is JS and cannot be asserted here —
// the memory note about the preview pane is exactly that. What can be: which branch of the page rendered,
// which actions the rules allow, and the one write ACT authors into a live terminal.
public class SessionViewTests : ComponentTest
{
    [Fact]
    public void An_id_that_is_not_there_says_so()
    {
        var cut = Render<SessionView>(p => p.Add(c => c.CardId, Guid.NewGuid()));

        cut.Find("div.session.missing").TextContent.Should().Contain("no longer exists");
        cut.FindAll("div.terminal").Should().BeEmpty();
    }

    // The header is the only thing on this page that says which task the terminal belongs to, so a card
    // stored without a title falls back to its prompt here like everywhere else.
    [Fact]
    public async Task An_untitled_card_is_named_by_its_prompt()
    {
        var card = Lineage(1042, string.Empty, BoardColumn.Ready);

        card.InitialPrompt = "Rename the widget everywhere";

        await BoardWith(card);

        var cut = Render<SessionView>(p => p.Add(c => c.CardId, card.Id));

        cut.Find("header.bar span.title").TextContent.Should().Be("Rename the widget everywhere");
    }

    // No xterm at all on an archived card, rather than an empty one: a black pane that never paints reads as
    // a terminal that failed to start, which is the one thing this page must not say about work that is over.
    [Fact]
    public async Task An_archived_card_states_that_there_is_no_terminal()
    {
        var cut = await Open(BoardColumn.Completed, archived: true);

        cut.Find("div.terminal.closed").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.FindAll("div.screen").Should().BeEmpty();
        cut.FindAll("div.idle").Should().BeEmpty("the pane beside it already says why there is none");
    }

    [Fact]
    public async Task An_archived_card_offers_no_action_at_all()
    {
        var cut = await Open(BoardColumn.Completed, archived: true);

        cut.FindAll("div.actions button").Should().BeEmpty();
    }

    // Every action here is refused by its own rule, so the rail is where those rules become visible.
    [Fact]
    public async Task A_card_that_has_never_run_is_offered_a_launch_and_nothing_else()
    {
        var cut = await Open(BoardColumn.Ready);

        Actions(cut).Should().Equal("Launch now");
        cut.Find("div.idle").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_card_wanting_the_user_is_offered_the_sign_off()
    {
        var cut = await Open(BoardColumn.YourTurn);

        Actions(cut).Should().Contain("Mark completed");
    }

    // The sign-off taken back, offered only where it applies.
    [Fact]
    public async Task A_signed_off_card_is_offered_the_undo()
    {
        var cut = await Open(BoardColumn.Completed);

        Actions(cut).Should().Contain("Reopen");
        Actions(cut).Should().NotContain("Mark completed");
    }

    [Fact]
    public async Task A_card_with_a_session_can_have_its_terminal_restarted()
    {
        var cut = await Open(BoardColumn.YourTurn);

        Actions(cut).Should().Contain("Restart terminal");
    }

    [Fact]
    public async Task A_card_with_no_session_has_no_terminal_to_restart()
    {
        var cut = await Open(BoardColumn.Ready);

        Actions(cut).Should().NotContain("Restart terminal");
    }

    // Null forever for an agent with no desktop app, so the action simply does not appear.
    [Fact]
    public async Task The_handoff_shows_only_where_the_adapter_answers_with_a_url()
    {
        var claude = await Open(BoardColumn.YourTurn);

        Actions(claude).Should().Contain("Open in desktop app");

        var codex = await Open(BoardColumn.YourTurn, agent: AgentType.Codex);

        Actions(codex).Should().NotContain("Open in desktop app");
    }

    [Fact]
    public async Task The_handoff_hands_the_url_to_the_shell()
    {
        var cut = await Open(BoardColumn.YourTurn);

        Act(cut, "Open in desktop app");

        Desktop.External.Should().ContainSingle().Which.Should().StartWith("mock://resume?session=");
    }

    // Not on an archived card: handing the session to the agent's desktop app is still resuming it, in a
    // window ACT does not own.
    [Fact]
    public async Task An_archived_card_is_not_handed_over_either()
    {
        var cut = await Open(BoardColumn.Completed, archived: true);

        Actions(cut).Should().NotContain("Open in desktop app");
    }

    // Signing off ends the session, which would leave this view showing an empty pane for work that is done.
    [Fact]
    public async Task Signing_off_returns_to_the_board()
    {
        var cut = await Open(BoardColumn.YourTurn);

        Act(cut, "Mark completed");

        cut.WaitForAssertion(() => Route.Should().BeEmpty());
        Board.Card(Card.Id)!.Column.Should().Be(BoardColumn.Completed);
    }

    // Unlike the sign-off this stays on the page: the terminal it reopened into is the reason the user took
    // the completion back.
    [Fact]
    public async Task Reopening_stays_on_the_page()
    {
        var cut = await Open(BoardColumn.Completed);

        Act(cut, "Reopen");

        cut.WaitForAssertion(() => Board.Card(Card.Id)!.Column.Should().Be(BoardColumn.YourTurn));
        Route.Should().Be($"card/{Card.Id}/terminal");
    }

    [Fact]
    public async Task A_launch_that_never_started_is_reported()
    {
        Claude.Fails = new InvalidOperationException("claude is not on PATH");

        var cut = await Open(BoardColumn.Ready);

        cut.Find("div.actions button").Click();

        cut.WaitForAssertion(() => Notifications.Messages.Should().ContainSingle());
        Notifications.Messages[0].Detail.Should().Be("claude is not on PATH");
    }

    // The store's copy is a fresh object on every write, so the view has to take the new one rather than hold
    // the instance it was initialized with.
    [Fact]
    public async Task The_rail_follows_the_card_as_it_moves_underneath()
    {
        var cut = await Open(BoardColumn.Executing);

        cut.FindAll("header.bar span.badge").Should().BeEmpty();

        var card = Board.Card(Card.Id)!;
        card.Badge = Badge.NeedsPermission;

        await Board.UpdateAsync(card);

        cut.WaitForAssertion(() =>
            cut.Find("header.bar span.badge").TextContent.Should().Be("needs permission"));
    }

    [Fact]
    public async Task The_rail_states_what_the_run_was_given()
    {
        var cut = await Open(BoardColumn.Executing);

        var facts = cut.Find("dl.facts").TextContent;

        facts.Should().Contain("claude").And.Contain("/dev/act");
    }

    [Fact]
    public async Task A_session_that_has_not_reported_its_id_yet_says_pending()
    {
        var cut = await Open(BoardColumn.Ready);

        cut.Find("dl.facts").TextContent.Should().Contain("reported at session start");
    }

    // The card's own list, not the folder's contents: a file dropped into the terminal mid-session belongs to
    // the conversation rather than to the task record.
    [Fact]
    public async Task The_rail_lists_the_files_the_launch_handed_over()
    {
        var cut = await Open(BoardColumn.Executing, attachment: "shot.png");

        cut.Find("ul.files .name").TextContent.Should().Be("shot.png");
    }

    [Fact]
    public async Task A_card_with_no_files_gets_no_row_for_them()
    {
        var cut = await Open(BoardColumn.Executing);

        cut.FindAll("ul.files").Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_whose_bytes_are_gone_says_so_rather_than_offering_to_open_it()
    {
        var cut = await Open(BoardColumn.Executing, attachment: "shot.png", onDisk: false);

        var row = cut.Find("ul.files button.open");

        row.ClassList.Should().Contain("gone");
        row.QuerySelector("i")!.TextContent.Should().Be("broken_image");
    }

    // Works on an archived card too: the files outlive the session, and opening one changes nothing.
    [Fact]
    public async Task An_archived_card_files_can_still_be_opened()
    {
        var cut = await Open(BoardColumn.Completed, archived: true, attachment: "shot.png");

        cut.Find("ul.files button.open").Click();

        Desktop.Opened.Should().ContainSingle().Which.Should().EndWith("shot.png");
    }

    [Fact]
    public async Task Only_an_image_that_is_still_there_previews_on_hover()
    {
        var cut = await Open(BoardColumn.Executing, attachment: "shot.png");

        cut.Find("ul.files li").MouseEnter();

        cut.Find("div.preview img").GetAttribute("src").Should().Contain("shot.png");
    }

    // Nothing to type into, so nothing is written and the refusal says why — the alternative is a file saved
    // into a folder with no agent to read the path.
    [Fact]
    public async Task A_drop_with_no_live_session_is_refused()
    {
        var cut = await Open(BoardColumn.Ready);

        Drop(cut, "notes.md");

        Notifications.Messages.Should().ContainSingle();
        Attachments.CardIds().Should().BeEmpty();
    }

    // The one write ACT authors into a live terminal, and deliberately the least it can be: the file's own
    // path, quoted when it has a space in it, and a trailing space — never a submit key.
    [Fact]
    public async Task A_drop_into_a_live_session_types_the_path_and_stops()
    {
        var cut = await OpenLive();

        Drop(cut, "notes.md");

        var typed = Typed().Should().ContainSingle().Subject;

        typed.Should().EndWith(" ").And.Contain("notes.md");
        typed.Should().NotContain("\r").And.NotContain("\n");
    }

    [Fact]
    public async Task A_path_with_a_space_in_it_is_quoted()
    {
        var cut = await OpenLive();

        Drop(cut, "my notes.md");

        Typed().Should().ContainSingle().Which.Should().StartWith("\"").And.Contain("my notes.md");
    }

    [Fact]
    public async Task Two_files_are_typed_as_one_line()
    {
        var cut = await OpenLive();

        Drop(cut, "one.md", "two.md");

        Typed().Should().ContainSingle()
            .Which.Should().Contain("one.md").And.Contain("two.md");
    }

    // `GetMultipleFiles` throws past its maximum and an exception out of an `InputFile` handler
    // takes the circuit down — so a drop past the cap has to be refused with a toast, exactly as
    // the task form refuses it, rather than crashing the session page.
    [Fact]
    public async Task A_drop_of_more_files_than_the_cap_is_refused_with_a_warning()
    {
        var cut = await OpenLive();

        Drop(cut, [.. Enumerable.Range(0, TaskAttachment.MaxPerTask + 1).Select(index => $"file-{index}.md")]);

        Notifications.Messages.Should().ContainSingle()
            .Which.Summary.Should().Be(Strings.NewTask_Attachments_TooMany);
        Typed().Should().BeEmpty("nothing was saved, so nothing may be typed");
        Attachments.CardIds().Should().BeEmpty();
    }

    // Into the folder the launch already granted the CLI access to, and *not* onto the card: the prompt is
    // the opening instruction and stays verbatim.
    [Fact]
    public async Task A_file_handed_over_mid_session_does_not_join_the_card_own_list()
    {
        var cut = await OpenLive();

        Drop(cut, "notes.md");

        Attachments.CardIds().Should().ContainSingle(cardId => cardId == Card.Id);
        Board.Card(Card.Id)!.Attachments.Should().BeEmpty();
        cut.FindAll("ul.files").Should().BeEmpty();
    }

    // Lineage reads both directions on the rail, each a link — the next question is always "what
    // was that one?".
    [Fact]
    public async Task The_rail_links_the_parent_and_every_follow_up()
    {
        var parent = Lineage(1040, "The plan", BoardColumn.YourTurn);

        Card = Lineage(1042, "Rename the widget", BoardColumn.Executing);
        Card.SessionId = "c0ffee00-0000-4a2c-9f4d-2f0a3f7c1e22";
        Card.Origin = TaskOrigin.Spawned;
        Card.ParentId = parent.Id;

        var child = Lineage(1043, "Migrate the tests", BoardColumn.Ready);
        child.ParentId = Card.Id;

        parent.Children.Add(Card.Id);
        Card.Children.Add(child.Id);

        await BoardWith(parent, Card, child);

        var cut = Render<SessionView>(p => p.Add(c => c.CardId, Card.Id));

        var links = cut.FindAll("dl a");

        links.Should().HaveCount(2);
        links[0].TextContent.Should().Contain("#1040").And.Contain("The plan");
        links[0].GetAttribute("href").Should().Contain(parent.Id.ToString());
        links[1].TextContent.Should().Contain("#1043").And.Contain("Migrate the tests");
        links[1].GetAttribute("href").Should().Contain(child.Id.ToString());
    }

    // Resolved per render off the live board on purpose: creating a follow-up while its parent's
    // page is open is the point of the tool, and the rail has to show it without a reload.
    [Fact]
    public async Task A_follow_up_created_while_the_page_is_open_appears_on_the_rail()
    {
        var cut = await OpenLive();

        cut.FindAll("dl a").Should().BeEmpty();

        await FollowUps.CreateAsync(Card.Id, new FollowUpRequest("Clean up", "Do it."));

        cut.WaitForAssertion(() => cut.FindAll("dl a").Should().ContainSingle()
            .Which.TextContent.Should().Contain("Clean up"));
    }

    // Two terminals are the same component, so following a lineage link is a parameter change on
    // this instance rather than a fresh page — the view has to reload for the card the link named,
    // or the click changes the url and nothing else.
    [Fact]
    public async Task Following_the_spawned_by_link_opens_the_parent()
    {
        var parent = Lineage(1040, "The plan", BoardColumn.YourTurn);

        Card = Lineage(1042, "Rename the widget", BoardColumn.Executing);
        Card.Origin = TaskOrigin.Spawned;
        Card.ParentId = parent.Id;
        parent.Children.Add(Card.Id);

        await BoardWith(parent, Card);

        var cut = Render<SessionView>(p => p.Add(c => c.CardId, Card.Id));

        cut.Find("header.bar span.title").TextContent.Should().Be("Rename the widget");

        cut.Render(p => p.Add(c => c.CardId, parent.Id));

        cut.WaitForAssertion(() =>
        {
            cut.Find("header.bar span.id").TextContent.Should().Be("#1040");
            cut.Find("header.bar span.title").TextContent.Should().Be("The plan");
        });

        var link = cut.FindAll("dl a").Should().ContainSingle().Subject;

        link.TextContent.Should().Contain("#1042");
        link.GetAttribute("href").Should().Contain(Card.Id.ToString());
    }

    // The old card's session must not stay bound under the new card's face: what the rail says and
    // what a drop types into have to belong to the card being shown.
    [Fact]
    public async Task Following_a_lineage_link_rebinds_the_terminal_to_the_card_it_opened()
    {
        var cut = await OpenLive();

        var parent = Lineage(1040, "The plan", BoardColumn.YourTurn);

        await BoardWith(parent);

        cut.Render(p => p.Add(c => c.CardId, parent.Id));

        cut.WaitForAssertion(() => cut.Find("div.idle").TextContent.Should().NotBeNullOrWhiteSpace());

        Drop(cut, "notes.md");

        Notifications.Messages.Should().ContainSingle()
            .Which.Summary.Should().Be(Strings.Session_AttachNoSession);
        Typed().Should().BeEmpty("the spawned card's session is no longer the one on screen");
    }

    private static Card Lineage(int number, string title, BoardColumn column) => new()
    {
        Number = number,
        Title = title,
        Column = column,
        AgentType = AgentType.ClaudeCode,
        WorkingDir = "/dev/act",
        Schedule = TaskSchedule.Manual,
        LaunchConfig = new LaunchConfig { Model = MockAgentAdapter.FastModel },
    };

    private static IReadOnlyList<string> Actions(IRenderedComponent<SessionView> cut)
        => [.. cut.FindAll("div.actions button").Select(RadzenDom.ButtonText)];

    private static void Act(IRenderedComponent<SessionView> cut, string action)
        => cut.FindAll("div.actions button").Single(button => RadzenDom.ButtonText(button) == action).Click();

    private static void Drop(IRenderedComponent<SessionView> cut, params string[] names)
        => cut.FindComponent<InputFile>().UploadFiles(
            [.. names.Select(name => InputFileContent.CreateFromText("contents", name))]);

    // What ACT actually typed at the agent — recorded by the mock session rather than sent to a pty.
    private IReadOnlyList<string> Typed()
        => [.. ((MockAgentSession)Registry.For(Card.Id)!).Received
            .Where(input => input.Kind is AgentInputKind.Write)
            .Select(input => input.Text!)];

    private Card Card { get; set; } = default!;

    private async Task<IRenderedComponent<SessionView>> Open(
        BoardColumn column,
        bool archived = false,
        AgentType agent = AgentType.ClaudeCode,
        string? attachment = null,
        bool onDisk = true)
    {
        Card = new Card
        {
            Number = 1042,
            Title = "Rename the widget",
            Column = column,
            AgentType = agent,
            WorkingDir = "/dev/act",
            Schedule = TaskSchedule.Manual,
            LaunchConfig = new LaunchConfig { Model = MockAgentAdapter.FastModel },
            SessionId = column is BoardColumn.Ready ? null : "c0ffee00-0000-4a2c-9f4d-2f0a3f7c1e22",
            DeletedAt = archived ? Now.AddDays(-1) : null,
        };

        if (attachment is { } fileName)
        {
            Card.Attachments = [new TaskAttachment { FileName = fileName, Length = 2048 }];

            if (onDisk)
                await Attachments.SaveAsync(Card.Id, fileName, new MemoryStream([1, 2, 3]));
        }

        await BoardWith(Card);

        Navigation.NavigateTo($"/card/{Card.Id}/terminal");

        return Render<SessionView>(p => p.Add(c => c.CardId, Card.Id));
    }

    // A card the registry really holds a session for, which is the only state the drop can write into.
    private async Task<IRenderedComponent<SessionView>> OpenLive()
    {
        var cut = await Open(BoardColumn.Executing);

        await Launcher.RestartAsync(Board.Card(Card.Id)!, TerminalSize.Default);

        return cut;
    }
}
