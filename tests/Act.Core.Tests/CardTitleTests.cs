using Act.Core.Agents;
using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

// What a card is *called*, which stopped being what it stores the day title generation became optional.
// Two properties carry it: a stored title always wins, and a blank one is never shown as a blank — the
// prompt's opening words stand in, derived at render time rather than frozen into the card.
public class CardTitleTests
{
    [Fact]
    public void A_stored_title_is_what_the_card_is_called()
    {
        CardTitle.Of(Card("My own words", "Rename the widget everywhere")).Should().Be("My own words");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void An_untitled_card_is_called_by_the_start_of_its_prompt(string title)
    {
        CardTitle.Of(Card(title, "Rename the widget everywhere"))
            .Should().Be("Rename the widget everywhere");
    }

    // The same answer the save used to write, so switching generation off changes what is *stored* without
    // changing what the board reads.
    [Fact]
    public void The_stand_in_is_the_first_line_of_the_prompt_unwrapped()
    {
        CardTitle.Of(Card(string.Empty, "## **Rename the widget**\n\nThen update the tests"))
            .Should().Be("Rename the widget");
    }

    [Fact]
    public void The_stand_in_is_shortened_like_any_other_title()
    {
        var title = CardTitle.Of(Card(string.Empty, string.Join(' ', Enumerable.Repeat("word", 60))));

        title.Split(' ').Should().HaveCount(TaskTitleQuery.MaxWords);
    }

    // Not reachable through the form — a card needs a title or a prompt to be saved at all — but this is the
    // last thing standing between a nameless strip and the board, so it answers rather than throwing.
    [Fact]
    public void A_card_with_neither_a_title_nor_a_prompt_is_called_nothing()
    {
        CardTitle.Of(Card(string.Empty, "  ")).Should().BeEmpty();
    }

    private static Card Card(string title, string prompt) => new()
    {
        Title = title,
        InitialPrompt = prompt,
        Column = BoardColumn.Preparing,
    };
}
