using Act.Core.Model;
using Act.Core.Rules;
using AwesomeAssertions;

namespace Act.Core.Tests;

public class WindowPlacementTests
{
    private const int MinWidth = 1000;

    private const int MinHeight = 320;

    private static readonly ScreenArea Laptop = new(0, 0, 1920, 1040);

    // The external monitor sits to the right of the laptop, which is where Windows puts a second
    // screen by default and where the saved coordinates of an undocked window come from.
    private static readonly ScreenArea External = new(1920, 0, 2560, 1400);

    [Fact]
    public void A_window_left_inside_a_screen_comes_back_untouched()
    {
        var placed = Fit(new WindowBounds { X = 200, Y = 100, Width = 1400, Height = 800 }, Laptop);

        placed.X.Should().Be(200);
        placed.Y.Should().Be(100);
        placed.Width.Should().Be(1400);
        placed.Height.Should().Be(800);
    }

    // The docked case: unplug the wide monitor and every saved coordinate is off the side of the
    // only screen left.
    [Fact]
    public void A_window_left_on_a_screen_that_is_gone_opens_centred_on_the_one_that_remains()
    {
        var placed = Fit(new WindowBounds { X = 2400, Y = 300, Width = 1600, Height = 900 }, Laptop);

        placed.X.Should().Be(160);
        placed.Y.Should().Be(70);
        placed.Width.Should().Be(1600);
        placed.Height.Should().Be(900);
    }

    [Fact]
    public void A_window_bigger_than_the_screen_shrinks_to_it()
    {
        var placed = Fit(new WindowBounds { X = 2000, Y = 40, Width = 2400, Height = 1300 }, Laptop);

        placed.Width.Should().Be(1920);
        placed.Height.Should().Be(1040);
        placed.X.Should().Be(0);
        placed.Y.Should().Be(0);
    }

    // Half off the right edge is still off: the part that hangs over cannot be clicked, and the
    // fix is to slide it back, not to resize it.
    [Fact]
    public void A_window_hanging_off_an_edge_slides_back_inside()
    {
        var placed = Fit(new WindowBounds { X = 1700, Y = 900, Width = 1200, Height = 700 }, Laptop);

        placed.X.Should().Be(720);
        placed.Y.Should().Be(340);
        placed.Width.Should().Be(1200);
        placed.Height.Should().Be(700);
    }

    [Fact]
    public void A_window_left_on_the_second_screen_stays_on_it_while_it_is_there()
    {
        var placed = Fit(new WindowBounds { X = 2200, Y = 300, Width = 1600, Height = 900 }, Laptop, External);

        placed.X.Should().Be(2200);
        placed.Y.Should().Be(300);
        placed.Width.Should().Be(1600);
        placed.Height.Should().Be(900);
    }

    [Fact]
    public void A_screen_smaller_than_the_window_minimum_does_not_shrink_it_past_that()
    {
        var placed = Fit(new WindowBounds { X = 0, Y = 0, Width = 1200, Height = 700 }, new ScreenArea(0, 0, 800, 600));

        placed.Width.Should().Be(MinWidth);
        placed.Height.Should().Be(600);
        placed.X.Should().Be(0);
        placed.Y.Should().Be(0);
    }

    [Fact]
    public void A_maximized_window_stays_maximized()
        => Fit(new WindowBounds { X = 9000, Y = 9000, Width = 1400, Height = 800, Maximized = true }, Laptop)
            .Maximized.Should().BeTrue();

    [Fact]
    public void Nothing_is_moved_when_no_screen_is_known()
    {
        var placed = WindowPlacement.Fit(
            new WindowBounds { X = 2400, Y = 300, Width = 1600, Height = 900 }, [], MinWidth, MinHeight);

        placed.X.Should().Be(2400);
        placed.Y.Should().Be(300);
    }

    private static WindowBounds Fit(WindowBounds wanted, params ScreenArea[] screens)
        => WindowPlacement.Fit(wanted, screens, MinWidth, MinHeight);
}
