using Act.Core.Model;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// S2 — a task is created and stays created.
//
// The form is covered six ways over in bUnit; what is new here is everything underneath it. The board
// runs on `FakeCardStore` in every component test, so this is the first time the real `BsonMapper`, the
// real schema and the counter document that mints card numbers are involved in anything.
public sealed class TaskCreationTests : BrowserTest
{
    [Fact]
    public async Task A_task_created_in_the_browser_lands_on_the_board()
    {
        await GoAsync();

        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(0);

        await Page.Locator("button.newtaskbtn").ClickAsync();

        await FillAsync("Rename the widget", "Rename it everywhere", "/dev/act");

        await Page.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(1);

        var card = App.Board.In(BoardColumn.Preparing).Should().ContainSingle().Subject;

        card.Title.Should().Be("Rename the widget");
        card.InitialPrompt.Should().Be("Rename it everywhere");
        card.WorkingDir.Should().Be("/dev/act");
    }

    // The counter document, which only exists in the real store. A card with no number is a card the
    // board cannot name and the user cannot refer to.
    [Fact]
    public async Task A_new_task_is_given_a_number()
    {
        await GoAsync();

        await CreateAsync("First task");

        var first = App.Board.In(BoardColumn.Preparing).Single();

        first.Number.Should().BeGreaterThan(0);

        await CreateAsync("Second task");

        var numbers = App.Board.In(BoardColumn.Preparing).Select(card => card.Number).ToList();

        numbers.Should().OnlyHaveUniqueItems();
    }

    // Through LiteDB and back out again. A reload drops the circuit and everything in it, so what comes
    // back is what was really written to disk.
    [Fact]
    public async Task It_is_still_there_after_a_reload()
    {
        await GoAsync();

        await CreateAsync("Rename the widget");

        await GoAsync();

        await Assertions.Expect(Page.Locator("div.col div.strip")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.Locator("div.col div.strip .ttl")).ToHaveTextAsync("Rename the widget");
    }

    // Reached by url rather than by clicking, and read back out of the store rather than out of the
    // circuit that created it.
    [Fact]
    public async Task It_can_be_opened_again_by_its_own_url()
    {
        await GoAsync();

        await CreateAsync("Rename the widget", prompt: "Rename it everywhere");

        var id = App.Board.In(BoardColumn.Preparing).Single().Id;

        await GoAsync($"/card/{id}/edit");

        await Assertions.Expect(Page.Locator("header.bar span.title")).ToHaveTextAsync("Rename the widget");
        await Assertions.Expect(Page.Locator("textarea")).ToHaveValueAsync("Rename it everywhere");
    }

    // The board is a list of lanes, and a new task lands in the first one — the only column that offers a
    // way to make one.
    [Fact]
    public async Task A_new_task_lands_where_new_tasks_land()
    {
        await GoAsync();

        await CreateAsync("Rename the widget");

        var lane = Page.Locator("div.col").First;

        await Assertions.Expect(lane.Locator("div.strip")).ToHaveCountAsync(1);
        await Assertions.Expect(lane.Locator(".colcount")).ToHaveTextAsync("1");
    }

    private async Task CreateAsync(string title, string prompt = "Do the work", string workingDir = "/dev/act")
    {
        await Page.Locator("button.newtaskbtn").ClickAsync();

        await FillAsync(title, prompt, workingDir);

        await Page.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(Page.Locator("div.board")).ToBeVisibleAsync();
    }

    private async Task FillAsync(string title, string prompt, string workingDir)
    {
        await Assertions.Expect(Page.Locator("div.taskview")).ToBeVisibleAsync();

        await Page.Locator("div.titlerow input").FillAsync(title);
        await Page.Locator("textarea").FillAsync(prompt);
        await Page.Locator("div.dirrow input").FillAsync(workingDir);
    }
}
