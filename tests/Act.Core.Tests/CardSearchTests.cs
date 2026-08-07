using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class CardSearchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_query_is_not_a_filter(string? query)
    {
        CardSearch.IsActive(query).Should().BeFalse();
        CardSearch.Matches(Card(), query).Should().BeTrue();
        CardSearch.Filter([Card(), Card()], query).Should().HaveCount(2);
    }

    [Fact]
    public void The_title_matches_on_a_substring()
        => CardSearch.Matches(Card(title: "Fix the pty quoting"), "quot").Should().BeTrue();

    [Fact]
    public void So_does_the_working_directory()
        => CardSearch.Matches(Card(workingDir: @"C:\Dev\north-lab-kev\act"), "north-lab").Should().BeTrue();

    [Fact]
    public void And_the_prompt_which_is_where_the_recall_value_is()
        => CardSearch.Matches(
            Card(prompt: "Take the argument escaping over from the transport"),
            "escaping").Should().BeTrue();

    [Fact]
    public void Nothing_else_is_searched()
        => CardSearch.Matches(Card(observedModel: "claude-sonnet-5"), "sonnet").Should().BeFalse();

    [Fact]
    public void Case_is_folded()
        => CardSearch.Matches(Card(title: "Fix the PTY quoting"), "pty").Should().BeTrue();

    [Fact]
    public void So_are_accents_because_french_is_a_shipped_language()
    {
        CardSearch.Matches(Card(title: "Réviser la façade"), "reviser").Should().BeTrue();
        CardSearch.Matches(Card(title: "Reviser la facade"), "façade").Should().BeTrue();
    }

    [Fact]
    public void Every_term_has_to_match_somewhere()
    {
        var card = Card(title: "Fix the pty quoting", workingDir: @"C:\Dev\act");

        CardSearch.Matches(card, "pty act").Should().BeTrue();
        CardSearch.Matches(card, "pty codex").Should().BeFalse();
    }

    [Fact]
    public void Terms_may_be_separated_by_any_run_of_whitespace()
        => CardSearch.Matches(Card(title: "Fix the pty quoting"), "  fix\tquoting ").Should().BeTrue();

    [Theory]
    [InlineData("#1042")]
    [InlineData("1042")]
    [InlineData("#104")]
    [InlineData("104")]
    [InlineData("1")]
    public void A_numeric_term_matches_the_number_by_prefix(string query)
        => CardSearch.Matches(Card(number: 1042), query).Should().BeTrue();

    [Fact]
    public void But_not_from_the_middle_of_it()
        => CardSearch.Matches(Card(number: 1042), "042").Should().BeFalse();

    // The number rule adds a way to match, it does not take the term away from the text fields.
    [Fact]
    public void A_numeric_term_still_reaches_the_text()
        => CardSearch.Matches(Card(number: 1042, title: "Bump to 2.1.220"), "220").Should().BeTrue();

    [Fact]
    public void A_hash_that_is_not_a_number_is_just_text()
    {
        CardSearch.Matches(Card(title: "Closes #17 upstream"), "#17").Should().BeTrue();
        CardSearch.Matches(Card(number: 1042, title: "Nothing to see"), "#abc").Should().BeFalse();
    }

    [Fact]
    public void A_query_matching_nothing_yields_an_empty_list()
        => CardSearch.Filter([Card(title: "Fix the pty quoting")], "codex").Should().BeEmpty();

    // The board's order is the column's and the archive's is newest-first, so filtering must hand
    // back what it was given, minus the misses.
    [Fact]
    public void Filtering_preserves_the_order_it_was_given()
    {
        var cards = new[]
        {
            Card(number: 1003, title: "pty three"),
            Card(number: 1001, title: "codex one"),
            Card(number: 1002, title: "pty two"),
        };

        CardSearch.Filter(cards, "pty").Select(card => card.Number).Should().Equal(1003, 1002);
    }

    private static Card Card(
        int number = 1000,
        string title = "A task",
        string workingDir = @"C:\Dev\act",
        string prompt = "Do the thing",
        string? observedModel = null) => new()
        {
            Number = number,
            Title = title,
            WorkingDir = workingDir,
            InitialPrompt = prompt,
            ObservedModel = observedModel,
        };
}
