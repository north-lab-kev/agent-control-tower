using AwesomeAssertions;
using Microsoft.Playwright;

namespace Act.App.E2eTests;

// `act-attach` — 214 lines, and until now nothing tested any of it. bUnit stubs JS, so every component
// test can prove is that the page *asked* for the module; what the module then does with a drag, a drop
// or a clipboard is only observable in a browser.
//
// Three routes into one input: the picker already works on its own, so the module feeds that same
// `<input type="file">` for the other two rather than inventing a second transport. Everything below is
// a check that a file arrived through Blazor's own chunked upload — which is what a chip on the form
// means.
public sealed class AttachTests : BrowserTest
{
    private async Task GoToFormAsync()
    {
        await GoAsync("/card/new");
        await Clipboard.WaitForReadyAsync(Page);
    }

    [Fact]
    public async Task A_dropped_file_is_attached()
    {
        await GoToFormAsync();

        await Clipboard.DropAsync(Page, "notes.md");

        await Assertions.Expect(Page.Locator("div.chips div.chip span.name")).ToHaveTextAsync("notes.md");
    }

    [Fact]
    public async Task Several_dropped_files_all_arrive()
    {
        await GoToFormAsync();

        await Clipboard.DropAsync(Page, "one.md", "two.md", "three.md");

        await Assertions.Expect(Page.Locator("div.chips div.chip")).ToHaveCountAsync(3);
    }

    // The browser's default for a dropped file is to navigate to it, and on a Blazor Server page that
    // discards the circuit and everything the user has typed. The module blocks it at the document.
    [Fact]
    public async Task A_drop_never_gets_to_navigate()
    {
        await GoToFormAsync();

        var prevented = await Clipboard.DropAsync(Page, "notes.md");

        prevented.Should().BeTrue("the module has to stop the browser opening the file");
    }

    [Fact]
    public async Task Dragging_files_over_the_page_lights_the_drop_zone()
    {
        await GoToFormAsync();

        await Assertions.Expect(Page.Locator("div.dropzone.over")).ToHaveCountAsync(0);

        await Clipboard.DragAsync(Page, "dragenter");

        await Assertions.Expect(Page.Locator("div.dropzone.over")).ToBeVisibleAsync();

        await Clipboard.DragAsync(Page, "dragleave");

        await Assertions.Expect(Page.Locator("div.dropzone.over")).ToHaveCountAsync(0);
    }

    // The counter, and the reason it exists: a drag over a child fires `dragleave` on the parent, so a
    // boolean flickers the highlight off and on across every element under the cursor.
    [Fact]
    public async Task Dragging_across_the_page_does_not_flicker_the_highlight()
    {
        await GoToFormAsync();

        await Clipboard.DragAsync(Page, "dragenter");
        await Assertions.Expect(Page.Locator("div.dropzone.over")).ToBeVisibleAsync();

        // Into a child and out of it again — the cursor never left the page.
        await Clipboard.DragAsync(Page, "dragenter", "div.dropzone");
        await Clipboard.DragAsync(Page, "dragleave", "div.dropzone");

        await Assertions.Expect(Page.Locator("div.dropzone.over")).ToBeVisibleAsync();
    }

    // The fallback that exists because a screenshot copied on Windows reaches the page as an item with no
    // file list at all — without it the paste route fails silently on the thing people paste most.
    [Fact]
    public async Task An_image_that_is_only_an_item_is_still_attached()
    {
        await GoToFormAsync();

        await Clipboard.PasteImageOnlyAsync(Page);

        await Assertions.Expect(Page.Locator("div.chips div.chip span.name")).ToHaveTextAsync("pasted.png");
    }

    // One picture, one attachment. A screenshot tool puts it on the clipboard twice — a saved file and a
    // raw bitmap — and reading both would attach it as two. `files` wins outright, and it is the better
    // of the two because it carries the tool's own name instead of one the browser invented.
    [Fact]
    public async Task A_picture_on_the_clipboard_twice_is_attached_once()
    {
        await GoToFormAsync();

        await Clipboard.PasteFileAndBitmapAsync(Page, "screenpresso.png", "image.png");

        await Assertions.Expect(Page.Locator("div.chips div.chip")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.Locator("div.chips div.chip span.name")).ToHaveTextAsync("screenpresso.png");
    }

    // The subtlest rule in the codebase. Copying a cell from Excel or a selection from Word puts *both* a
    // bitmap and text on the clipboard, and a user pasting that into the prompt box means the text. The
    // fallback yields; a populated `files` would not, because that is unambiguous.
    [Fact]
    public async Task An_image_pasted_beside_text_yields_to_the_text()
    {
        await GoToFormAsync();

        await Clipboard.PasteImageBesideTextAsync(Page, "some copied text");

        // Nothing to wait for, so give the upload a chance to be wrong before believing it.
        await Page.WaitForTimeoutAsync(500);

        await Assertions.Expect(Page.Locator("div.chips div.chip")).ToHaveCountAsync(0);
    }

    // `place` is JS because the box is `position: fixed` — the only way out of a scroll container's
    // clipping — and a fixed box needs viewport coordinates nothing in CSS can hand it. It also arrives
    // hidden, so it never paints at the viewport's corner on its way to where it belongs.
    [Fact]
    public async Task The_hover_preview_is_placed_and_stays_on_screen()
    {
        await GoToFormAsync();

        await Clipboard.DropAsync(Page, "shot.png");
        await Assertions.Expect(Page.Locator("div.chips div.chip")).ToHaveCountAsync(1);

        await Page.Locator("div.chips div.chip").HoverAsync();

        var preview = Page.Locator("div.preview");

        await Assertions.Expect(preview).ToBeVisibleAsync();

        var box = await preview.BoundingBoxAsync();
        var viewport = Page.ViewportSize!;

        box.Should().NotBeNull();
        box!.X.Should().BeGreaterThanOrEqualTo(0);
        box.Y.Should().BeGreaterThanOrEqualTo(0);
        (box.X + box.Width).Should().BeLessThanOrEqualTo(viewport.Width);
        (box.Y + box.Height).Should().BeLessThanOrEqualTo(viewport.Height);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_never_previews()
    {
        await GoToFormAsync();

        await Clipboard.DropAsync(Page, "notes.md");
        await Assertions.Expect(Page.Locator("div.chips div.chip")).ToHaveCountAsync(1);

        await Page.Locator("div.chips div.chip").HoverAsync();
        await Page.WaitForTimeoutAsync(300);

        await Assertions.Expect(Page.Locator("div.preview")).ToHaveCountAsync(0);
    }
}
