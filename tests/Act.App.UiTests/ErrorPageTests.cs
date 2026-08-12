using Act.App.Components.Pages;
using AwesomeAssertions;
using Bunit;

namespace Act.App.UiTests;

// The last-resort page. Its one conditional is the request id, which is there so a user can quote something
// when they report a failure — and absent rather than blank when there is nothing to quote, because an empty
// `<code>` block reads as the page itself being broken.
public class ErrorPageTests : ComponentTest
{
    [Fact]
    public void It_says_what_happened_without_pretending_to_know_why()
    {
        var cut = Render<Error>();

        cut.Find("div.errpage h1").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.Find("div.errpage h2").TextContent.Should().NotBeNullOrWhiteSpace();
    }

    // No `Activity` and no `HttpContext` in a test, which is also the state of a Blazor circuit that failed
    // after the response was sent.
    [Fact]
    public void With_nothing_to_quote_there_is_no_request_id()
    {
        Render<Error>().FindAll("code").Should().BeEmpty();
    }

    [Fact]
    public void An_activity_id_is_what_the_user_is_asked_to_quote()
    {
        using var activity = new System.Diagnostics.Activity("test").Start();

        Render<Error>().Find("code").TextContent.Should().Be(activity.Id);
    }

    [Fact]
    public void The_not_found_page_states_itself()
    {
        Render<NotFound>().Markup.Should().NotBeNullOrWhiteSpace();
    }
}
