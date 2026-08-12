using Microsoft.Playwright;

namespace Act.App.E2eTests;

// Drags, drops and pastes, synthesised in the page. There is no other way to reach `act-attach`: a real
// OS drag cannot be driven from a test, and `SetInputFilesAsync` would feed the file input directly and
// skip the whole module.
//
// Two shapes are built here, and the difference is the point. A **real `DataTransfer`** covers a drop
// and an unambiguous file paste — the browser fills `files` for both. A **hand-made object** covers the
// clipboard the module was actually written for: a bitmap that appears only as an `item`, with `files`
// empty and text alongside it, which is what copying a cell out of Excel puts on the Windows clipboard.
// That shape cannot be built with `new DataTransfer()`, because `items.add` populates `files` too.
internal static class Clipboard
{
    // The module is imported in `OnAfterRenderAsync`, so it binds some time *after* the page paints —
    // the prerendered HTML looks finished long before anything is listening. Every spec here would
    // otherwise race that, and lose often enough to be useless.
    //
    // The gate is the module answering: dispatch a drag until the highlight appears, which nothing but
    // a bound `watch` can produce. Then an empty drop resets the depth counter to zero and clears the
    // highlight — `onDrop` returns before it pushes anything when the file list is empty.
    internal static async Task WaitForReadyAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            """
            () => {
                const root = document.querySelector('[id^="act-drop-"]');
                if (!root) {
                    return false;
                }

                const transfer = new DataTransfer();
                Object.defineProperty(transfer, 'types', { value: ['Files'] });

                root.dispatchEvent(new DragEvent('dragenter', { dataTransfer: transfer, bubbles: true }));

                return document.querySelector('.dropzone.over') !== null;
            }
            """,
            null,
            new PageWaitForFunctionOptions { PollingInterval = 100 });

        await page.EvaluateAsync(
            """
            () => {
                const root = document.querySelector('[id^="act-drop-"]');

                root.dispatchEvent(new DragEvent('drop', {
                    dataTransfer: new DataTransfer(),
                    bubbles: true,
                    cancelable: true,
                }));
            }
            """);
    }

    private const string MakeTransfer = """
        (names) => {
            const transfer = new DataTransfer();

            for (const name of names) {
                transfer.items.add(new File(['file contents'], name, { type: 'application/octet-stream' }));
            }

            return transfer;
        }
        """;

    internal static Task<bool> DropAsync(IPage page, params string[] names)
        => page.EvaluateAsync<bool>(
            $$"""
            (names) => {
                const transfer = ({{MakeTransfer}})(names);
                const root = document.querySelector('[id^="act-drop-"]');
                const event = new DragEvent('drop', { dataTransfer: transfer, bubbles: true, cancelable: true });

                root.dispatchEvent(event);

                return event.defaultPrevented;
            }
            """,
            names);

    // `types` is all a drag can read — the file list is deliberately empty until the drop — so the
    // highlight has only that to go on.
    internal static Task DragAsync(IPage page, string type, string selector = "[id^=\"act-drop-\"]")
        => page.EvaluateAsync(
            """
            ([type, selector]) => {
                const transfer = new DataTransfer();
                transfer.items.add('x', 'text/plain');

                const target = document.querySelector(selector);

                Object.defineProperty(transfer, 'types', { value: ['Files'] });

                target.dispatchEvent(new DragEvent(type, { dataTransfer: transfer, bubbles: true }));
            }
            """,
            new[] { type, selector });

    // A screenshot tool puts the same picture on the clipboard twice — a saved file *and* a raw bitmap.
    // `files` has to win, and win outright rather than being concatenated with `items`, or one image
    // arrives as two attachments and the worse of the two names is kept.
    //
    // Hand-made rather than a real `DataTransfer`, for the reason above the class: `items.add` populates
    // `files` too, so the two cannot be made to disagree any other way.
    internal static Task PasteFileAndBitmapAsync(IPage page, string fileName, string bitmapName)
        => page.EvaluateAsync(
            """
            ([fileName, bitmapName]) => {
                const clipboard = {
                    types: ['Files'],
                    files: [new File(['png bytes'], fileName, { type: 'image/png' })],
                    getData: () => '',
                    items: [{
                        kind: 'file',
                        getAsFile: () => new File(['png bytes'], bitmapName, { type: 'image/png' }),
                    }],
                };

                const event = new Event('paste', { bubbles: true, cancelable: true });

                Object.defineProperty(event, 'clipboardData', { value: clipboard });

                document.dispatchEvent(event);
            }
            """,
            new[] { fileName, bitmapName });

    // The measured Windows shape: an image reachable only through `items`, `files` empty, and text on the
    // clipboard beside it. `ClipboardEvent`'s own `clipboardData` cannot be given this, so the event
    // carries a defined property instead.
    internal static Task PasteImageBesideTextAsync(IPage page, string text)
        => page.EvaluateAsync(
            """
            (text) => {
                const clipboard = {
                    types: ['text/plain', 'Files'],
                    files: [],
                    getData: kind => (kind === 'text/plain' ? text : ''),
                    items: [{
                        kind: 'file',
                        getAsFile: () => new File(['png bytes'], 'pasted.png', { type: 'image/png' }),
                    }],
                };

                const event = new Event('paste', { bubbles: true, cancelable: true });

                Object.defineProperty(event, 'clipboardData', { value: clipboard });

                document.dispatchEvent(event);
            }
            """,
            text);

    // The same shape with nothing to yield to, so the fallback really does read `items`.
    internal static Task PasteImageOnlyAsync(IPage page)
        => page.EvaluateAsync(
            """
            () => {
                const clipboard = {
                    types: ['Files'],
                    files: [],
                    getData: () => '',
                    items: [{
                        kind: 'file',
                        getAsFile: () => new File(['png bytes'], 'pasted.png', { type: 'image/png' }),
                    }],
                };

                const event = new Event('paste', { bubbles: true, cancelable: true });

                Object.defineProperty(event, 'clipboardData', { value: clipboard });

                document.dispatchEvent(event);
            }
            """);
}
