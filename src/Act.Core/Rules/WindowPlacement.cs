using Act.Core.Model;

namespace Act.Core.Rules;

public readonly record struct ScreenArea(int X, int Y, int Width, int Height);

// Where a remembered window may reopen. The screens it was last left on are not the screens it
// comes back to: a laptop undocked from a wide monitor keeps bounds that are off the side of the
// world, and one docked to a bigger one keeps a size the new screen dwarfs. Every restored window
// is pulled back inside a work area that exists now, whole and reachable.
public static class WindowPlacement
{
    public static WindowBounds Fit(
        WindowBounds wanted,
        IReadOnlyList<ScreenArea> screens,
        int minWidth,
        int minHeight)
    {
        if (screens.Count == 0)
            return wanted.Copy();

        var screen = Nearest(wanted, screens);

        var width = Span(wanted.Width, minWidth, screen.Width);
        var height = Span(wanted.Height, minHeight, screen.Height);

        // Nothing of it lands on a live screen, so its old corner says nothing about where the user
        // wants it — the middle of the nearest screen is the honest answer.
        if (!screens.Any(area => Overlaps(wanted, area)))
            return Centre(screen, width, height, wanted.Maximized);

        return new WindowBounds
        {
            X = Origin(wanted.X, screen.X, width, screen.Width),
            Y = Origin(wanted.Y, screen.Y, height, screen.Height),
            Width = width,
            Height = height,
            Maximized = wanted.Maximized,
        };
    }

    private static WindowBounds Centre(ScreenArea screen, int width, int height, bool maximized) => new()
    {
        X = screen.X + ((screen.Width - width) / 2),
        Y = screen.Y + ((screen.Height - height) / 2),
        Width = width,
        Height = height,
        Maximized = maximized,
    };

    private static ScreenArea Nearest(WindowBounds wanted, IReadOnlyList<ScreenArea> screens)
        => screens
            .OrderByDescending(area => Intersection(wanted, area))
            .ThenBy(area => Distance(wanted, area))
            .First();

    private static bool Overlaps(WindowBounds wanted, ScreenArea area) => Intersection(wanted, area) > 0;

    private static long Intersection(WindowBounds wanted, ScreenArea area)
    {
        var width = Math.Min(wanted.X + wanted.Width, area.X + area.Width) - Math.Max(wanted.X, area.X);
        var height = Math.Min(wanted.Y + wanted.Height, area.Y + area.Height) - Math.Max(wanted.Y, area.Y);

        return width <= 0 || height <= 0 ? 0 : (long)width * height;
    }

    private static long Distance(WindowBounds wanted, ScreenArea area)
    {
        var x = (wanted.X + (wanted.Width / 2)) - (area.X + (area.Width / 2));
        var y = (wanted.Y + (wanted.Height / 2)) - (area.Y + (area.Height / 2));

        return ((long)x * x) + ((long)y * y);
    }

    // A screen smaller than the window's own minimum is the one case the minimum wins: the window
    // cannot be made to fit, and shrinking it past what its bar needs would break the layout too.
    private static int Span(int wanted, int minimum, int available)
        => Math.Max(minimum, Math.Min(wanted, available));

    private static int Origin(int wanted, int start, int span, int available)
        => available <= span ? start : Math.Clamp(wanted, start, start + available - span);
}
