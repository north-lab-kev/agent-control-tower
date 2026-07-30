using System.Globalization;
using Act.App.Resources;
using Act.Core.Model;
using AwesomeAssertions;

namespace Act.App.UiTests;

// The resource key for a transition is the enum name by convention rather than by a switch, which is
// only safe if something checks the convention holds. This is that something: a new
// `TransitionReason` with no wording — or wording added in English and forgotten in French — fails
// here rather than showing a user a bare enum name.
public class TransitionTextTests
{
    private static readonly CultureInfo[] Shipped =
    [
        new("en"),
        new("fr"),
    ];

    [Fact]
    public void Every_reason_has_wording_in_every_shipped_language()
    {
        foreach (var culture in Shipped)
        {
            foreach (var reason in Enum.GetValues<TransitionReason>())
            {
                TransitionText.Resolve(reason, culture)
                    .Should().NotBeNullOrWhiteSpace($"{reason} needs wording in {culture.Name}");
            }
        }
    }

    // Wording differs per language; a French entry that is byte-identical to the English one is
    // almost always a forgotten translation rather than a genuine coincidence.
    [Fact]
    public void The_french_wording_is_actually_translated()
    {
        var untranslated = Enum.GetValues<TransitionReason>()
            .Where(reason => TransitionText.Resolve(reason, Shipped[0]) == TransitionText.Resolve(reason, Shipped[1]))
            .ToList();

        untranslated.Should().BeEmpty();
    }

    [Fact]
    public void The_verbatim_detail_is_placed_into_the_wording()
    {
        var transition = new Transition
        {
            Reason = TransitionReason.AgentExited,
            Note = "3",
        };

        TransitionText.For(transition, Shipped[0]).Should().Be("Agent exited with code 3");
        TransitionText.For(transition, Shipped[1]).Should().Contain("3");
    }

    [Fact]
    public void A_reason_that_takes_no_detail_renders_without_one()
        => TransitionText.For(new Transition { Reason = TransitionReason.SessionKilled }, Shipped[0])
            .Should().Be("Session killed");

    // Rows written before reasons existed carry only a note, and the timeline still has to show
    // something for them.
    [Fact]
    public void A_transition_with_no_reason_falls_back_to_its_note()
        => TransitionText.For(new Transition { Note = "Launched with adjustments: model x → y" }, Shipped[0])
            .Should().Be("Launched with adjustments: model x → y");

    [Fact]
    public void A_transition_with_neither_renders_empty_rather_than_throwing()
        => TransitionText.For(new Transition(), Shipped[0]).Should().BeEmpty();
}
