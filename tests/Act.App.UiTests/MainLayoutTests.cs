using Act.App.Components.Layout;
using Act.App.Components.Pages;
using Act.App.Notifications;
using Act.Core.Model;
using AwesomeAssertions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Act.App.UiTests;

// The shell around every page. Three of its four jobs are testable without a browser: the top bar's
// destinations, the two values it cascades down to whatever is rendering inside it, and the theme attribute
// the dialog mask depends on reaching `<body>`. The fourth — reporting whether anyone is looking — is what the
// notification gate reads, and it is why `UiPresence` is a service rather than a field.
public class MainLayoutTests : ComponentTest
{
    [Fact]
    public void The_top_bar_carries_the_three_destinations()
    {
        Show().FindAll("div.actions button").Should().HaveCount(3);
    }

    [Theory]
    [InlineData(0, "templates")]
    [InlineData(1, "archive")]
    [InlineData(2, "settings")]
    public void Each_one_goes_where_its_label_says(int button, string route)
    {
        var cut = Show();

        cut.FindAll("div.actions button")[button].Click();

        Route.Should().Be(route);
    }

    [Fact]
    public void The_bar_states_what_the_app_is()
    {
        var cut = Show();

        cut.Find("h6.wordmark").TextContent.Should().Be("ACT");
        cut.Find(".fullname").TextContent.Should().Contain("Agent Control Tower");
    }

    // The theme attribute is written on the *document* element by `App.razor`, and the stylesheet `media`
    // attributes are flipped rather than re-rendered — an interactive component's prerendered `<link>` would be
    // discarded and the browser would repaint unstyled. So what the layout owes a theme change is the interop
    // call, not markup, and that is what this pins.
    [Theory]
    [InlineData(ThemePreference.Dark, "dark")]
    [InlineData(ThemePreference.Light, "light")]
    public void A_theme_change_is_applied_through_the_document_rather_than_by_re_rendering(
        ThemePreference theme,
        string expected)
    {
        var cut = Show();

        Settings.SetTheme(theme);

        cut.WaitForAssertion(() => JSInterop.Invocations["actTheme.apply"].Should().ContainSingle());

        JSInterop.Invocations["actTheme.apply"][0].Arguments[0].Should().Be(expected);
    }

    [Fact]
    public void Choosing_the_theme_it_already_has_applies_nothing()
    {
        var cut = Show();

        Settings.SetTheme(ThemePreference.System);

        cut.WaitForAssertion(() => cut.Markup.Should().NotBeNullOrWhiteSpace());

        JSInterop.Invocations["actTheme.apply"].Should().BeEmpty();
    }

    // Density and blink are the board's, but they are read from settings once here and cascaded — so the board
    // needs no subscription of its own for them.
    [Fact]
    public void Density_and_blink_are_handed_down_to_whatever_is_inside()
    {
        Settings.SetDensity(BoardDensity.Compact);
        Settings.SetBlinkYourTurn(false);

        var cut = Show();

        var probe = cut.FindComponent<CascadeProbe>().Instance;

        probe.Density.Should().Be(BoardDensity.Compact);
        probe.BlinkYourTurn.Should().BeFalse();
    }

    [Fact]
    public void A_density_changed_elsewhere_reaches_the_page_inside()
    {
        var cut = Show();

        cut.FindComponent<CascadeProbe>().Instance.Density.Should().Be(BoardDensity.Detailed);

        Settings.SetDensity(BoardDensity.Compact);

        cut.WaitForAssertion(() =>
            cut.FindComponent<CascadeProbe>().Instance.Density.Should().Be(BoardDensity.Compact));
    }

    [Fact]
    public void The_quota_strip_is_part_of_the_bar()
    {
        Usage.Publish(UsageProbeResult.Of(new AgentUsage(
            AgentType.ClaudeCode,
            [new UsageWindow(UsageWindowKind.Session, 40, Now.AddHours(3))],
            Now,
            LimitReached: false,
            Plan: null)));

        Show().Find("div.usagegroup div.meter").Should().NotBeNull();
    }

    // Nobody is looking until the browser says the window has focus, which is what stops a toast being
    // suppressed for a window sitting behind another one.
    [Fact]
    public void Nobody_is_watching_the_board_until_the_window_reports_focus()
    {
        var cut = Show();

        Presence.BoardIsBeingWatched.Should().BeFalse();

        cut.Instance.OnPresence(true);

        Presence.BoardIsBeingWatched.Should().BeTrue();
    }

    [Fact]
    public void Looking_at_a_card_is_not_looking_at_the_board()
    {
        Navigation.NavigateTo("/card/new");

        var cut = Show();

        cut.Instance.OnPresence(true);

        Presence.BoardIsBeingWatched.Should().BeFalse();
    }

    // Reported per navigation, so walking off the board stops suppressing its toasts.
    [Fact]
    public void Leaving_the_board_stops_it_being_watched()
    {
        var cut = Show();

        cut.Instance.OnPresence(true);
        Presence.BoardIsBeingWatched.Should().BeTrue();

        Navigation.NavigateTo("/settings");

        Presence.BoardIsBeingWatched.Should().BeFalse();
    }

    [Fact]
    public async Task A_circuit_that_goes_away_stops_being_counted()
    {
        var cut = Show();

        cut.Instance.OnPresence(true);
        Presence.BoardIsBeingWatched.Should().BeTrue();

        await cut.Instance.DisposeAsync();

        Presence.BoardIsBeingWatched.Should().BeFalse();
    }

    // The way back from a clicked toast: a navigation on the live circuit rather than a page reload.
    [Fact]
    public void A_clicked_toast_navigates_the_circuit_to_that_card()
    {
        var taskId = Guid.NewGuid();
        var cut = Show();

        Services.GetRequiredService<DeepLinkRouter>().Open(taskId);

        cut.WaitForAssertion(() => Route.Should().Be($"card/{taskId}/terminal"));
    }

    private UiPresence Presence => Services.GetRequiredService<UiPresence>();

    private IRenderedComponent<MainLayout> Show()
        => Render<MainLayout>(p => p.Add(c => c.Body, builder =>
        {
            builder.OpenComponent<CascadeProbe>(0);
            builder.CloseComponent();
        }));

    // Renders nothing and only exists to be asked what the layout cascaded down to it — the two values the
    // board reads without subscribing to settings itself.
    private sealed class CascadeProbe : ComponentBase
    {
        [CascadingParameter(Name = "Density")]
        public BoardDensity Density { get; set; }

        [CascadingParameter(Name = "BlinkYourTurn")]
        public bool BlinkYourTurn { get; set; }
    }
}
