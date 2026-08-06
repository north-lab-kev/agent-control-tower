using Act.App.Components.Shared;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Act.App.UiTests;

// One control narrows both the board and the archive, so what it promises its parent is the whole of
// its contract: one `QueryChanged` per real change, and nothing at all when the text has not moved.
// The board re-filters every lane on that callback, which is why a repeat is worth refusing.
public class CardFilterTests : ComponentTest
{
    [Fact]
    public void Typing_hands_the_query_up()
    {
        var seen = new List<string>();
        var cut = Filter(string.Empty, seen);

        cut.Find("input.box").Input("widget");

        seen.Should().Equal("widget");
    }

    // Blazor raises `oninput` on its own schedule and a parent may re-render with the same value; a
    // filter that forwarded that would make the board refilter for nothing.
    [Fact]
    public void Setting_the_query_it_already_has_says_nothing()
    {
        var seen = new List<string>();
        var cut = Filter("widget", seen);

        cut.Find("input.box").Input("widget");

        seen.Should().BeEmpty();
    }

    [Fact]
    public void Escape_clears_it()
    {
        var seen = new List<string>();
        var cut = Filter("widget", seen);

        cut.Find("input.box").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        seen.Should().Equal(string.Empty);
    }

    [Fact]
    public void Any_other_key_leaves_it_alone()
    {
        var seen = new List<string>();
        var cut = Filter("widget", seen);

        cut.Find("input.box").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        seen.Should().BeEmpty();
    }

    [Fact]
    public void The_clear_button_clears_it()
    {
        var seen = new List<string>();
        var cut = Filter("widget", seen);

        cut.Find("button.clear").Click();

        seen.Should().Equal(string.Empty);
    }

    // Nothing to clear, so nothing offering to: the affordance is the only sign the box is holding a
    // query, and one that is always there says the board might be filtered when it is not.
    [Fact]
    public void There_is_nothing_to_clear_on_an_empty_box()
    {
        Filter(string.Empty, []).FindAll("button.clear").Should().BeEmpty();
    }

    [Fact]
    public void The_placeholder_doubles_as_the_label()
    {
        var box = Filter(string.Empty, [], "Filter tasks").Find("input.box");

        box.GetAttribute("placeholder").Should().Be("Filter tasks");
        box.GetAttribute("aria-label").Should().Be("Filter tasks");
    }

    private IRenderedComponent<CardFilter> Filter(
        string query,
        List<string> seen,
        string placeholder = "Search")
        => Render<CardFilter>(p => p
            .Add(c => c.Query, query)
            .Add(c => c.Placeholder, placeholder)
            .Add(c => c.QueryChanged, seen.Add));
}
