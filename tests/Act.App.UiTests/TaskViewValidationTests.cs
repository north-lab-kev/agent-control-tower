using Act.App.Components.Pages;
using Act.Core.Abstractions;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// Two refusals with different weights, which is the whole point. A path that cannot be a path blocks the
// save; a path that merely is not there yet does not, because a directory you are about to create is a
// perfectly reasonable thing to save a task against. Getting that backwards either lets a doomed card
// through or refuses a legitimate one.
public class TaskViewValidationTests : ComponentTest
{
    [Fact]
    public async Task A_malformed_directory_refuses_the_save_and_says_which_way()
    {
        Directories.Checks["relative/path"] = PathCheck.Malformed(PathError.NotAbsolute);

        var cut = Show();

        Fill(cut, prompt: "Do the thing", workingDir: "relative/path");

        await Submit(cut);

        Board.All.Should().BeEmpty("nothing was saved");
        cut.Markup.Should().Contain("Enter an absolute path");
    }

    [Fact]
    public async Task Nonsense_is_refused_differently_from_a_relative_path()
    {
        Directories.Checks["<>"] = PathCheck.Malformed(PathError.Malformed);

        var cut = Show();

        Fill(cut, prompt: "Do the thing", workingDir: "<>");

        await Submit(cut);

        cut.Markup.Should().Contain("not a valid folder path");
    }

    // Found here rather than at launch: a directory that does not exist is the single most common reason a
    // launch fails, and the form is where the user can still do something about it.
    [Fact]
    public async Task A_directory_that_is_only_missing_is_saved_anyway_and_offered_a_mkdir()
    {
        Directories.Checks["/dev/new"] = PathCheck.Missing("/dev/new");

        var cut = Show();

        Fill(cut, prompt: "Do the thing", workingDir: "/dev/new");

        cut.Find("div.dirwarn").TextContent.Should().Contain("does not exist yet");

        await Submit(cut);

        Board.All.Should().ContainSingle().Which.WorkingDir.Should().Be("/dev/new");
    }

    [Fact]
    public void The_mkdir_button_creates_the_directory_that_was_typed()
    {
        Directories.Checks["/dev/new"] = PathCheck.Missing("/dev/new");

        var cut = Show();

        Fill(cut, workingDir: "/dev/new");

        cut.Find("div.dirwarn button").Click();

        Directories.Created.Should().Equal("/dev/new");
    }

    [Fact]
    public void A_mkdir_that_the_os_refuses_is_reported_rather_than_thrown()
    {
        Directories.Checks["/dev/new"] = PathCheck.Missing("/dev/new");
        Directories.CreateFails = new UnauthorizedAccessException("Access to the path is denied.");

        var cut = Show();

        Fill(cut, workingDir: "/dev/new");

        cut.Find("div.dirwarn button").Click();

        Notifications.Messages.Should().ContainSingle()
            .Which.Detail.Should().Be("Access to the path is denied.");
    }

    // Shows the resolved path too, because the typed one and the real one differ whenever a `~` or a
    // forward slash is involved — and that difference is exactly what confuses people.
    [Fact]
    public void The_missing_warning_names_the_resolved_path_only_when_it_differs()
    {
        Directories.Checks["/dev/new"] = PathCheck.Missing("/dev/new");
        Directories.Checks["~/dev/new"] = PathCheck.Missing("/home/act/dev/new");

        var plain = Show();
        Fill(plain, workingDir: "/dev/new");
        plain.Find("div.dirwarn").TextContent.Should().NotContain("/dev/new");

        var tilde = Show();
        Fill(tilde, workingDir: "~/dev/new");
        tilde.Find("div.dirwarn").TextContent.Should().Contain("/home/act/dev/new");
    }

    [Fact]
    public void A_directory_that_is_there_warns_about_nothing()
    {
        var cut = Show();

        Fill(cut, workingDir: "/dev/act");

        cut.FindAll("div.dirwarn").Should().BeEmpty();
    }

    // A title is required; typing it is not. Blank passes only while the prompt can stand in — the same
    // condition `NewTaskForm.ApplyTo` falls back on, so what the field permits and what the card ends up
    // with cannot drift apart.
    [Fact]
    public async Task A_task_with_neither_title_nor_prompt_is_refused()
    {
        var cut = Show();

        Fill(cut, workingDir: "/dev/act");

        await Submit(cut);

        Board.All.Should().BeEmpty();
        cut.Markup.Should().Contain("Write a title");
    }

    [Fact]
    public async Task A_prompt_alone_is_enough_and_the_title_is_asked_for()
    {
        Claude.Answer = "Rename the widget";

        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Submit(cut);
        await Backfill.Idle;

        Board.All.Should().ContainSingle().Which.Title.Should().Be("Rename the widget");
    }

    // The save never waits on the CLI: the card lands on the board immediately under the prompt's
    // opening words, marked pending, and the agent's answer replaces them when it arrives.
    [Fact]
    public async Task The_save_returns_to_the_board_before_the_title_arrives()
    {
        Claude.QueryHeld = new TaskCompletionSource();
        Claude.Answer = "Rename the widget";

        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Submit(cut);

        Route.Should().BeEmpty();

        var saved = Board.All.Should().ContainSingle().Subject;

        saved.Title.Should().Be("Rename the widget everywhere");
        Backfill.IsPending(saved.Id).Should().BeTrue();

        Claude.QueryHeld.SetResult();

        await Backfill.Idle;

        Board.Card(saved.Id)!.Title.Should().Be("Rename the widget");
        Backfill.IsPending(saved.Id).Should().BeFalse();
    }

    [Fact]
    public async Task A_typed_title_asks_nobody()
    {
        var cut = Show();

        Fill(cut, title: "My own words", prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Submit(cut);

        Board.All.Should().ContainSingle().Which.Title.Should().Be("My own words");
        Claude.Queries.Should().BeEmpty();
    }

    // The one race a user can actually run: save untitled, reopen, type the real name before the
    // agent answers. The typed save withdraws the pending answer instead of being overwritten by it.
    [Fact]
    public async Task Saving_a_typed_title_withdraws_a_pending_backfill()
    {
        Claude.QueryHeld = new TaskCompletionSource();
        Claude.Answer = "Rename the widget";

        var create = Show();

        Fill(create, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Submit(create);

        var saved = Board.All.Should().ContainSingle().Subject;

        Backfill.IsPending(saved.Id).Should().BeTrue();

        var edit = Render<TaskView>(p => p.Add(c => c.CardId, saved.Id));

        Fill(edit, title: "My own words");

        await Submit(edit);

        Backfill.IsPending(saved.Id).Should().BeFalse();

        Claude.QueryHeld.SetResult();

        Board.Card(saved.Id)!.Title.Should().Be("My own words");
    }

    // Silent on save, unlike the button: the user was saving, not asking for a title, and a toast about how
    // the title was arrived at is an interruption they did not invite.
    [Fact]
    public async Task A_title_that_had_to_be_guessed_is_not_announced_on_a_save()
    {
        Claude.QueryFails = new InvalidOperationException("claude is not on PATH");

        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Submit(cut);
        await Backfill.Idle;

        Board.All.Should().ContainSingle().Which.Title.Should().NotBeNullOrWhiteSpace();
        Notifications.Messages.Should().BeEmpty();
    }

    // The one place a failure is worth a word: the user asked a question here, and a box that fills with
    // the prompt's opening words with no explanation looks like a bug rather than a fallback.
    [Fact]
    public void A_title_the_button_could_not_generate_says_so()
    {
        Claude.QueryFails = new InvalidOperationException("claude is not on PATH");

        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere");

        cut.Find("div.titlerow button").Click();

        Notifications.Messages.Should().ContainSingle();
        TaskViewTests.Title(cut).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void There_is_nothing_to_generate_a_title_from_without_a_prompt()
    {
        Show().Find("div.titlerow button").HasAttribute("disabled").Should().BeTrue();
    }

    // Explicit intent, so it overwrites: a user who clicks this while a title is already there is asking for
    // a different one, and refusing would leave the button doing nothing on the very card it was pressed on.
    [Fact]
    public void Asking_for_a_title_overwrites_the_one_that_is_there()
    {
        Claude.Answer = "A better title";

        var cut = Show();

        Fill(cut, title: "My own words", prompt: "Rename the widget everywhere");

        cut.Find("div.titlerow button").Click();

        TaskViewTests.Title(cut).Should().Be("A better title");
    }

    // The switch, on the one path it governs: a save with the box empty. Nothing is asked of an agent and
    // nothing is written in the user's place — `CardTitle` is what keeps the strip readable.
    [Fact]
    public async Task A_save_with_no_title_asks_nobody_when_titles_are_not_generated()
    {
        Settings.SetGenerateTitles(false);

        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere", workingDir: "/dev/act");

        await Submit(cut);

        var saved = Board.All.Should().ContainSingle().Subject;

        saved.Title.Should().BeEmpty();
        Backfill.IsPending(saved.Id).Should().BeFalse();
        Claude.Queries.Should().BeEmpty();
    }

    // Still required to *have* one, because the stand-in comes from the prompt: a card with neither is
    // nameless whatever the setting says.
    [Fact]
    public async Task A_task_with_neither_title_nor_prompt_is_refused_when_titles_are_not_generated()
    {
        Settings.SetGenerateTitles(false);

        var cut = Show();

        Fill(cut, workingDir: "/dev/act");

        await Submit(cut);

        Board.All.Should().BeEmpty();
        cut.Markup.Should().Contain("Write a title");
    }

    // The button is the user asking, which the switch has nothing to say about — it governs what ACT does
    // on its own account.
    [Fact]
    public void The_button_still_writes_a_title_when_titles_are_not_generated()
    {
        Settings.SetGenerateTitles(false);
        Claude.Answer = "Rename the widget";

        var cut = Show();

        Fill(cut, prompt: "Rename the widget everywhere");

        cut.Find("div.titlerow button").Click();

        TaskViewTests.Title(cut).Should().Be("Rename the widget");
    }

    // The placeholder is the only thing left saying what a blank box does, so it has to follow the
    // setting: "written from the prompt" is a promise nothing keeps once generation is off.
    [Fact]
    public void The_title_placeholder_follows_the_generation_setting()
    {
        Placeholder(Show()).Should().Be("Written from the prompt if you leave it blank");

        Settings.SetGenerateTitles(false);

        Placeholder(Show()).Should().Be("Optional");
    }

    private static string Placeholder(IRenderedComponent<TaskView> cut)
        => cut.Find("div.titlerow input").GetAttribute("placeholder") ?? string.Empty;

    // An untitled card opens with an empty box rather than a title nobody typed, and the heading falls back
    // to the prompt so the page still says which task it is.
    [Fact]
    public async Task An_untitled_card_opens_with_an_empty_box_under_its_prompt()
    {
        Settings.SetGenerateTitles(false);

        var card = TaskViewTests.Card(BoardColumn.Ready);
        card.Title = string.Empty;

        await BoardWith(card);

        var cut = Render<TaskView>(p => p.Add(c => c.CardId, card.Id));

        TaskViewTests.Title(cut).Should().BeEmpty();
        cut.Find("header.bar .title").TextContent.Should().Be("Rename the widget everywhere");
    }

    private static void Fill(
        IRenderedComponent<TaskView> cut,
        string? title = null,
        string? prompt = null,
        string? workingDir = null)
    {
        if (title is not null)
            cut.Find("div.titlerow input").Change(title);

        if (prompt is not null)
            cut.Find("textarea").Change(prompt);

        if (workingDir is not null)
            cut.Find("div.dirrow input").Change(workingDir);
    }

    // Submitted through the form rather than by clicking Save, because the button is a submit and the rule
    // being tested is the form's — a box still submits on Enter whatever the footer offers.
    private static Task Submit(IRenderedComponent<TaskView> cut) => cut.Find("form").SubmitAsync();

    private IRenderedComponent<TaskView> Show() => Render<TaskView>();
}
