using Act.App.Components.Shared;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// The tabs are `NavLink`s to three routes rather than a tab container, which is what keeps "which one
// am I on" out of C# entirely. That only holds if the hrefs are right and the match is exact — a
// `NavLinkMatch.Prefix` would light up Task on every face, since `/card/{id}/edit` is not a prefix of
// the others but the component is one edit away from being written that way.
public class CardTabsTests : ComponentTest
{
    private static readonly Guid CardId = new("2f6d1c40-0000-4b21-9a55-6d1f9c3ea100");

    [Fact]
    public void Every_face_of_a_card_is_a_link_of_its_own()
    {
        var tabs = Render<CardTabs>(p => p.Add(c => c.CardId, CardId)).FindAll("a.tab");

        tabs.Should().HaveCount(3);

        tabs.Select(tab => tab.GetAttribute("href")).Should().Equal(
            $"/card/{CardId}/edit",
            $"/card/{CardId}/terminal",
            $"/card/{CardId}/timeline");
    }

    // `Route` is what the rest of the app navigates by, so it has to agree with what this renders —
    // two spellings of the same three urls is how a tab and its own destination drift apart.
    [Theory]
    [InlineData(CardSurface.Task, "edit")]
    [InlineData(CardSurface.Terminal, "terminal")]
    [InlineData(CardSurface.Timeline, "timeline")]
    public void The_route_helper_names_the_same_url_the_tab_links_to(CardSurface surface, string tail)
    {
        CardTabs.Route(CardId, surface).Should().Be($"/card/{CardId}/{tail}");

        Render<CardTabs>(p => p.Add(c => c.CardId, CardId))
            .FindAll("a.tab")
            .Should().Contain(tab => tab.GetAttribute("href")!.EndsWith(tail, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("terminal")]
    [InlineData("timeline")]
    public void Only_the_face_being_looked_at_is_marked_active(string face)
    {
        Navigation.NavigateTo($"/card/{CardId}/{face}");

        var active = Render<CardTabs>(p => p.Add(c => c.CardId, CardId))
            .FindAll("a.tab")
            .Where(tab => tab.ClassList.Contains("active"))
            .ToList();

        active.Should().ContainSingle()
            .Which.GetAttribute("href").Should().Be($"/card/{CardId}/{face}");
    }

    // The exact match is the whole reason the tabs need no state: a card's terminal is not the task
    // face with something appended, and nothing else in the app should light one up either.
    [Fact]
    public void A_route_that_is_not_one_of_the_three_lights_nothing_up()
    {
        Navigation.NavigateTo("/");

        Render<CardTabs>(p => p.Add(c => c.CardId, CardId))
            .FindAll("a.tab.active").Should().BeEmpty();
    }
}
