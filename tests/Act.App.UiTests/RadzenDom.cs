using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Act.App.UiTests;

// Radzen's rendered shapes, named once. Two things make this worth having rather than repeating a
// `.rz-*` selector in twenty tests: the plan's rule is that assertions read the *app's* classes, and
// these are the exception that has to live somewhere; and a dropdown renders its whole item list into
// the markup whether or not it is open, which is the one Radzen fact every choice test depends on.
//
// A control is found by the words beside it rather than by position, so adding a field above does not
// renumber every test. `container` is how a labelled control is scoped: the task form gives its rows a
// class of its own, while the settings page leans on Radzen's stack — hence `SettingsRow`.
internal static class RadzenDom
{
    internal const string FormRow = "div.row";

    internal const string SettingsRow = "div.rz-justify-content-space-between";

    internal static IElement Row<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Rows(cut, container).FirstOrDefault(row => Labelled(row, label))
            ?? throw new InvalidOperationException($"No row labelled '{label}'. Rows: {Labels(cut, container)}");

    internal static bool HasRow<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Rows(cut, container).Any(row => Labelled(row, label));

    // What the closed dropdown shows — the selected item's text, or the placeholder when nothing is
    // selected.
    internal static string Selected<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Dropdown(cut, label, container).QuerySelector("span.rz-dropdown-label")!.TextContent.Trim();

    // Every item it offers. Present in the DOM whether the panel is open or not, which is what lets a
    // test read the list without driving the popup.
    internal static IReadOnlyList<string> Options<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => [.. Dropdown(cut, label, container).QuerySelectorAll("li.rz-dropdown-item span")
            .Select(item => item.TextContent.Trim())];

    internal static void Choose<T>(IRenderedComponent<T> cut, string label, string option, string container = FormRow)
        where T : IComponent
    {
        var wanted = Dropdown(cut, label, container).QuerySelectorAll("li.rz-dropdown-item")
                .FirstOrDefault(item => item.TextContent.Trim() == option)
            ?? throw new InvalidOperationException(
                $"'{label}' does not offer '{option}'. It offers: {string.Join(", ", Options(cut, label, container))}");

        wanted.Click();
    }

    internal static bool IsDisabled<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Dropdown(cut, label, container).GetAttribute("aria-disabled") == "true";

    internal static IElement Dropdown<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Row(cut, label, container).QuerySelector(".rz-dropdown")
            ?? throw new InvalidOperationException($"The row labelled '{label}' holds no dropdown.");

    internal static IElement Switch<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Row(cut, label, container).QuerySelector(".rz-switch")
            ?? throw new InvalidOperationException($"The row labelled '{label}' holds no switch.");

    internal static bool IsOn<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Switch(cut, label, container).ClassList.Contains("rz-switch-checked");

    internal static void Toggle<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Switch(cut, label, container).Click();

    internal static IElement Numeric<T>(IRenderedComponent<T> cut, string label, string container = FormRow)
        where T : IComponent
        => Row(cut, label, container).QuerySelector(".rz-numeric input")
            ?? throw new InvalidOperationException($"The row labelled '{label}' holds no number box.");

    // A button's own words, past the icon ligature `RadzenButton` renders beside them — `TextContent` on a
    // Radzen button reads "play_arrowLaunch now".
    internal static string ButtonText(IElement button)
        => button.QuerySelector("span.rz-button-text")?.TextContent.Trim() ?? button.TextContent.Trim();

    // Filled rather than outlined, which is all a `Variant` leaves in the markup — and the only way to
    // read which of two buttons a page put its weight behind.
    internal static bool IsFilled(IElement button) => button.ClassList.Contains("rz-variant-flat");

    // `IsBusy` swaps a button's own icon and label for a `refresh` glyph with a rotation animation
    // inlined on it. The animation is the tell rather than the missing label, which an icon-only
    // button lacks anyway.
    internal static bool IsBusy(IElement button)
        => button.QuerySelector("i[style*='rotation']") is not null;

    private static bool Labelled(IElement row, string label)
        => row.TextContent.TrimStart().StartsWith(label, StringComparison.Ordinal);

    private static IReadOnlyList<IElement> Rows<T>(IRenderedComponent<T> cut, string container)
        where T : IComponent
        => [.. cut.FindAll(container)];

    private static string Labels<T>(IRenderedComponent<T> cut, string container)
        where T : IComponent
        => string.Join(" | ", Rows(cut, container).Select(row => row.TextContent.Trim().Split('\n')[0]));
}
