using Act.App.Components.Pages;
using Act.Core.Model;
using AngleSharp.Dom;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// Deleting from the task page obeys the same `CardDeletePrompt` the board's strip does. It did not, and
// that was a real gap rather than a style difference: the same card, deleted from its own page instead of
// from the board, ended a running agent with nothing asked. `BoardViewTests` covers the strip; this covers
// the other door into the same delete, because a rule with two callers is only held by testing both.
public class TaskViewDeleteTests : ComponentTest
{
    [Theory]
    [InlineData(BoardColumn.Executing)]
    [InlineData(BoardColumn.YourTurn)]
    public async Task A_card_with_a_live_agent_asks_first_and_stays_until_answered(BoardColumn column)
    {
        var cut = await Open(column);

        Delete(cut).Click();

        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());
        Board.In(column).Should().ContainSingle("nothing goes until the question is answered");
    }

    [Fact]
    public async Task Confirming_deletes_the_card_and_leaves_the_page()
    {
        var cut = await Open(BoardColumn.Executing);

        Delete(cut).Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, true);

        cut.WaitForAssertion(() => Board.Archived.Should().ContainSingle());

        Board.In(BoardColumn.Executing).Should().BeEmpty();
        Route.Should().BeEmpty("the page goes back to the board once its card is gone");
    }

    // Both ways of saying no, and the null matters most: dismissing with the X or the overlay must read as
    // "keep it", because reading it as consent costs a running agent.
    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Declining_keeps_the_card_and_stays_on_the_page(bool? answer)
    {
        var cut = await Open(BoardColumn.Executing);

        Delete(cut).Click();
        cut.WaitForAssertion(() => DialogsOpened.Should().ContainSingle());

        await AnswerDialog(cut, answer);

        cut.WaitForAssertion(() => Board.In(BoardColumn.Executing).Should().ContainSingle());

        Board.Archived.Should().BeEmpty();
    }

    // A card that never launched loses nothing but a draft, so it goes straight to the archive — the same
    // answer the board gives, and the reason the prompt asks the rule rather than every caller deciding.
    [Theory]
    [InlineData(BoardColumn.Preparing)]
    [InlineData(BoardColumn.Ready)]
    [InlineData(BoardColumn.Completed)]
    public async Task A_card_with_no_live_agent_is_deleted_without_a_question(BoardColumn column)
    {
        var cut = await Open(column);

        Delete(cut).Click();

        cut.WaitForAssertion(() => Board.Archived.Should().ContainSingle());

        Board.In(column).Should().BeEmpty();
        DialogsOpened.Should().BeEmpty();
    }

    // First in the row of destructive actions — see `TaskViewLockTests`, which pins that a launched card
    // still offers all three.
    private static IElement Delete(IRenderedComponent<TaskView> cut) => cut.FindAll("div.destructive button")[0];

    private async Task<IRenderedComponent<TaskView>> Open(BoardColumn column)
    {
        var card = TaskViewTests.Card(column);

        await BoardWith(card);

        return Render<TaskView>(p => p.Add(c => c.CardId, card.Id));
    }
}
