// Three ways to hand a file to a page — a picker, a drop, a paste — and one way into Blazor.
//
// The picker already works on its own: `InputFile` renders a real `<input type="file">` and streams
// what it is given in chunks. So rather than invent a second transport for the other two, this
// module *feeds that same input*: it builds a `DataTransfer`, assigns it to `input.files` and
// dispatches `change`, and Blazor cannot tell the difference. The alternative — base64 over
// interop — would put a whole file in one SignalR message, which is the one shape the hub's message
// cap refuses.
//
// It also has to stop the browser doing what it does with a dropped file by default, which is to
// navigate to it: on a Blazor Server page that discards the circuit and everything the user typed.
// Hence the document-level `preventDefault` on both drag events for as long as anything is watching.

const zones = new Map();

// During a drag the file list is deliberately empty — only `types` is readable, which is what the
// highlight has to go on. `files` is populated on drop and nowhere else.
function dragCarriesFiles(transfer) {
    return transfer ? Array.from(transfer.types ?? []).includes('Files') : false;
}

// A drop always fills `files`. A *paste* cannot be relied on to: a bitmap copied from a screenshot
// tool reaches the page with no name and no path — the Windows clipboard carries `PNG`/`DIB`/
// `Format17` and no `FileDrop` at all — and it may only appear as a `kind: 'file'` **item** whose
// `File` the browser invents a name for. So `items` is read as a fallback, or the paste route would
// fail silently on the one thing people paste most.
//
// **The fallback yields to text**, and that is the part worth stating: copying a cell from Excel or
// a selection from Word puts *both* a bitmap and text on the clipboard, and the user pasting that
// into the prompt box or the terminal means the text. A populated `files` needs no such guard — that
// is an unambiguous file paste, the shape a file copied in Explorer arrives as.
//
// `files` **wins over** `items` rather than being concatenated with it, because a screenshot tool
// puts the same picture on the clipboard twice — a saved file *and* a raw bitmap (measured: a
// Screenpresso capture carries `FileDrop`, `Bitmap`, `FileContents` and an HTML `<img>` all at once).
// Reading both would attach one image as two files, and the `files` entry is the better of the two:
// it has the tool's own name on it instead of one the browser invented.
//
// Only real `File` objects are ever taken. The **HTML is deliberately never parsed**, even though it
// often names a local path (`<img src="file:///C:/…">`): that markup is clipboard content, not a
// grant, and honouring it would let whatever the user happens to paste name any file on the disk for
// ACT to copy and hand an agent. What the browser puts in `files`/`items` it has already gated.
function filesFrom(transfer, yieldToText = false) {
    if (transfer?.files?.length) {
        return Array.from(transfer.files);
    }

    if (yieldToText && (transfer?.getData('text/plain') ?? '').length > 0) {
        return [];
    }

    return Array.from(transfer?.items ?? [])
        .filter(item => item.kind === 'file')
        .map(item => item.getAsFile())
        .filter(Boolean);
}

function pushInto(inputId, files) {
    if (files.length === 0) {
        return false;
    }

    const input = document.getElementById(inputId);
    if (!input) {
        return false;
    }

    const transfer = new DataTransfer();
    files.forEach(file => transfer.items.add(file));

    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));

    return true;
}

function blockDefault(event) {
    event.preventDefault();
}

export function watch(rootId, inputId, owner) {
    const root = document.getElementById(rootId);
    if (!root || zones.has(rootId)) {
        return;
    }

    // A drag over a child fires `dragleave` on the parent, so a boolean flickers the highlight off
    // and on across every element under the cursor. Counting enters and leaves is what makes it
    // steady.
    let depth = 0;

    function report(active) {
        owner?.invokeMethodAsync('OnDragActive', active);
    }

    function onDragEnter(event) {
        if (!dragCarriesFiles(event.dataTransfer)) {
            return;
        }

        depth += 1;

        if (depth === 1) {
            report(true);
        }
    }

    function onDragLeave(event) {
        if (!dragCarriesFiles(event.dataTransfer) || depth === 0) {
            return;
        }

        depth -= 1;

        if (depth === 0) {
            report(false);
        }
    }

    function onDrop(event) {
        depth = 0;
        report(false);

        const files = filesFrom(event.dataTransfer);
        if (files.length === 0) {
            return;
        }

        event.preventDefault();
        pushInto(inputId, files);
    }

    // Bound on the document rather than the root, because a paste has no position: the caret may be
    // in a textarea, in the terminal, or nowhere. The guard is the whole contract with everything
    // else that wants a paste — **only a clipboard carrying files is taken**, so plain text still
    // reaches the prompt box and xterm exactly as it did.
    function onPaste(event) {
        const files = filesFrom(event.clipboardData, true);
        if (files.length === 0) {
            return;
        }

        if (pushInto(inputId, files)) {
            event.preventDefault();
            event.stopPropagation();
        }
    }

    root.addEventListener('dragenter', onDragEnter);
    root.addEventListener('dragleave', onDragLeave);
    root.addEventListener('drop', onDrop);

    // Capture, so a file paste is claimed before xterm's own textarea handler sees it.
    document.addEventListener('paste', onPaste, true);
    document.addEventListener('dragover', blockDefault);
    document.addEventListener('drop', blockDefault);

    zones.set(rootId, { root, onDragEnter, onDragLeave, onDrop, onPaste });
}

// Places the hover preview beside its chip. It has to be JS because the box is `position: fixed` — the
// only way out of the sheet's own `overflow-y: auto`, which clips an absolutely positioned overlay —
// and a fixed box needs viewport coordinates nothing in CSS can hand it.
//
// Below the chip when there is room, above it when there is not, and clamped to the viewport on both
// axes so a chip at the right-hand edge does not push half the thumbnail off screen. The box has a
// fixed size in CSS, so this runs *before* the image has loaded and never has to run twice.
const Gap = 8;

export function place(rootId) {
    const preview = document.getElementById(rootId)?.querySelector('.preview');
    const chip = preview?.closest('.chip');
    if (!preview || !chip) {
        return;
    }

    const anchor = chip.getBoundingClientRect();
    const width = preview.offsetWidth;
    const height = preview.offsetHeight;

    const below = anchor.bottom + Gap;
    const above = anchor.top - Gap - height;

    const top = below + height <= window.innerHeight - Gap
        ? below
        : (above >= Gap ? above : Math.max(Gap, window.innerHeight - Gap - height));

    const left = Math.min(Math.max(Gap, anchor.left), Math.max(Gap, window.innerWidth - Gap - width));

    preview.style.top = `${Math.round(top)}px`;
    preview.style.left = `${Math.round(left)}px`;
    preview.style.visibility = 'visible';
}

export function dispose(rootId) {
    const zone = zones.get(rootId);
    if (!zone) {
        return;
    }

    zone.root.removeEventListener('dragenter', zone.onDragEnter);
    zone.root.removeEventListener('dragleave', zone.onDragLeave);
    zone.root.removeEventListener('drop', zone.onDrop);

    document.removeEventListener('paste', zone.onPaste, true);
    document.removeEventListener('dragover', blockDefault);
    document.removeEventListener('drop', blockDefault);

    zones.delete(rootId);
}
