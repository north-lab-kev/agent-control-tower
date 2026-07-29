// xterm ships as UMD, so it is loaded on demand into the global scope rather than imported;
// that keeps it out of App.razor and off every page that never opens a terminal.

const handles = new Map();

function loadScript(src) {
    return new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[src="${src}"]`);
        if (existing) {
            resolve();
            return;
        }

        const element = document.createElement('script');
        element.src = src;
        element.onload = () => resolve();
        element.onerror = () => reject(new Error(`failed to load ${src}`));
        document.head.appendChild(element);
    });
}

function loadCss(href) {
    if (document.querySelector(`link[href="${href}"]`)) {
        return;
    }

    const element = document.createElement('link');
    element.rel = 'stylesheet';
    element.href = href;
    document.head.appendChild(element);
}

async function ensureXterm() {
    loadCss('/lib/xterm/xterm.css');

    if (!window.Terminal) {
        await loadScript('/lib/xterm/xterm.js');
    }

    if (!window.FitAddon) {
        await loadScript('/lib/xterm/addon-fit.js');
    }
}

// Read from the live `--act-*` tokens so the terminal flips with the rest of the control room
// instead of staying dark under the light theme.
function theme(host) {
    const style = getComputedStyle(host);
    const token = (name, fallback) => style.getPropertyValue(name).trim() || fallback;

    return {
        background: token('--act-term-bg', '#0e1620'),
        foreground: token('--act-ink', '#c8d4de'),
        cursor: token('--act-run', '#4fd1c5'),
        selectionBackground: token('--act-line2', '#2b3a4a'),
    };
}

export async function attach(elementId, owner) {
    await ensureXterm();

    const host = document.getElementById(elementId);
    if (!host) {
        return null;
    }

    const term = new window.Terminal({
        fontFamily: 'IBM Plex Mono, Cascadia Mono, Consolas, ui-monospace, monospace',
        fontSize: 12,
        cursorBlink: true,
        scrollback: 5000,
        allowProposedApi: true,
        theme: theme(host),
    });

    const fit = new window.FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open(host);

    // Fitting is only ever done when the proposed geometry actually differs. Calling fit()
    // unconditionally from a ResizeObserver is a feedback loop, and every resize makes the CLI
    // repaint its whole screen, so an unguarded version flickers and grows without bound.
    let applied = { cols: 0, rows: 0 };

    function refit(notify) {
        const proposed = fit.proposeDimensions();
        if (!proposed) {
            return false;
        }

        const { cols, rows } = proposed;
        if (!Number.isFinite(cols) || !Number.isFinite(rows) || cols < 2 || rows < 2) {
            return false;
        }

        if (cols === applied.cols && rows === applied.rows) {
            return true;
        }

        applied = { cols, rows };
        term.resize(cols, rows);

        if (notify) {
            owner.invokeMethodAsync('OnResize', cols, rows);
        }

        return true;
    }

    // The view can still be zero-sized on the first pass while the layout settles.
    if (!refit(false)) {
        await new Promise(resolve => setTimeout(resolve, 50));
        refit(false);
    }

    term.onData(data => owner.invokeMethodAsync('OnData', data));

    // Deliberately setTimeout and not requestAnimationFrame: a backgrounded tab stops painting,
    // rAF never fires, and the in-flight guard would latch on forever — leaving the terminal
    // permanently stuck at whatever size it opened with.
    let scheduled = 0;

    function scheduleRefit() {
        if (scheduled) {
            return;
        }

        scheduled = setTimeout(() => {
            scheduled = 0;

            try {
                refit(true);
            } catch {
                // the view is closing; nothing to resize
            }
        }, 60);
    }

    // Two triggers on purpose. ResizeObserver notifications are delivered as part of the
    // rendering lifecycle, so a page that is not painting never gets them; the window listener
    // still fires and covers the common case of the window itself changing size.
    const observer = new ResizeObserver(scheduleRefit);

    window.addEventListener('resize', scheduleRefit);

    observer.observe(host);
    handles.set(elementId, {
        term,
        fit,
        observer,
        cancelRefit: () => {
            clearTimeout(scheduled);
            window.removeEventListener('resize', scheduleRefit);
        },
    });
    term.focus();

    return { cols: term.cols, rows: term.rows };
}

export function write(elementId, text) {
    handles.get(elementId)?.term.write(text);
}

export function focus(elementId) {
    handles.get(elementId)?.term.focus();
}

export function dispose(elementId) {
    const handle = handles.get(elementId);
    if (!handle) {
        return;
    }

    handle.observer.disconnect();

    if (handle.cancelRefit) {
        handle.cancelRefit();
    }

    handle.term.dispose();
    handles.delete(elementId);
}
