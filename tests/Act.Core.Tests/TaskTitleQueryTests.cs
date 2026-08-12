using Act.Core.Agents;
using AwesomeAssertions;

namespace Act.Core.Tests;

// Everything a CLI might wrap a one-line answer in. None of this is hypothetical politeness: a model
// told to reply with a bare value quotes it, bolds it, bullets it, or introduces it, and each of those
// would otherwise land on the board verbatim.
public class TaskTitleQueryTests
{
    [Fact]
    public void The_prompt_asks_for_a_title_and_carries_the_request()
    {
        var prompt = TaskTitleQuery.PromptFor("  refactor the flight strip  ");

        prompt.Should().Contain("refactor the flight strip");
        prompt.Should().NotContain("  refactor");
    }

    [Theory]
    [InlineData("Consolidate badge colours", "Consolidate badge colours")]
    [InlineData("\"Consolidate badge colours\"", "Consolidate badge colours")]
    [InlineData("'Consolidate badge colours'", "Consolidate badge colours")]
    [InlineData("“Consolidate badge colours”", "Consolidate badge colours")]
    [InlineData("**Consolidate badge colours**", "Consolidate badge colours")]
    [InlineData("## Consolidate badge colours", "Consolidate badge colours")]
    [InlineData("- Consolidate badge colours", "Consolidate badge colours")]
    [InlineData("`Consolidate badge colours`", "Consolidate badge colours")]
    [InlineData("Consolidate badge colours.", "Consolidate badge colours")]
    [InlineData("  Consolidate   badge  colours  ", "Consolidate badge colours")]
    public void An_answer_is_unwrapped_down_to_the_title(string answer, string expected)
        => TaskTitleQuery.Clean(answer).Should().Be(expected);

    // A title that is asking or exclaiming is saying something; only a full stop is decoration.
    [Theory]
    [InlineData("Why does the board flicker?")]
    [InlineData("Fix the board, finally!")]
    public void Terminal_punctuation_that_carries_meaning_is_kept(string answer)
        => TaskTitleQuery.Clean(answer).Should().Be(answer);

    // The shape a chatty model produces, and the reason the *first* non-empty line wins rather than
    // the last: the explanation follows the answer far more often than it precedes it.
    [Fact]
    public void A_multi_line_answer_is_reduced_to_its_first_line()
        => TaskTitleQuery.Clean("Consolidate badge colours\n\nLet me know if you want it shorter.")
            .Should().Be("Consolidate badge colours");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n ")]
    public void An_empty_answer_is_no_answer(string? answer)
        => TaskTitleQuery.Clean(answer).Should().BeNull();

    [Fact]
    public void A_wordy_answer_is_cut_to_the_word_limit()
    {
        var wordy = string.Join(' ', Enumerable.Range(1, 60).Select(index => $"word{index}"));

        var title = TaskTitleQuery.Clean(wordy)!;

        title.Split(' ').Should().HaveCount(TaskTitleQuery.MaxWords);
    }

    // A single unbroken token — a model pasting a path or a url — clears the word limit and would
    // still overflow the field, which is why there are two limits rather than one.
    [Fact]
    public void A_single_enormous_word_is_cut_to_the_character_limit()
    {
        var title = TaskTitleQuery.Clean(new string('x', 500))!;

        title.Length.Should().Be(TaskTitleQuery.MaxLength);
    }

    [Fact]
    public void The_fallback_is_the_opening_of_the_prompt_and_obeys_the_same_limits()
    {
        var prompt = string.Join(' ', Enumerable.Range(1, 60).Select(index => $"word{index}"));

        var title = TaskTitleQuery.FromPrompt(prompt);

        title.Should().StartWith("word1 word2");
        title.Split(' ').Should().HaveCount(TaskTitleQuery.MaxWords);
    }

    // The fallback is the last thing between a failed query and an untitled card, so it has to answer
    // for a prompt whose every character is something `Unwrap` strips. It used to return empty here,
    // and an empty fallback is how a nameless card reached the board.
    [Theory]
    [InlineData("###")]
    [InlineData("\"\"")]
    [InlineData("- - -")]
    [InlineData("...")]
    [InlineData("*")]
    public void The_fallback_answers_even_for_a_prompt_made_only_of_punctuation(string prompt)
        => TaskTitleQuery.FromPrompt(prompt).Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void The_fallback_takes_the_prompts_first_meaningful_line()
        => TaskTitleQuery.FromPrompt("\n\n  Fix the timeline rail.\n\nThen do other things.")
            .Should().Be("Fix the timeline rail");
}
