// Whether the user is actually looking at this window. `document.hasFocus()` alone is not enough:
// a minimised or hidden window can still report focus, and a background tab reports visibility
// without it — the notification rule needs both to be true before it stays quiet.

let owner = null;

function report() {
    owner?.invokeMethodAsync('OnPresence', document.hasFocus() && document.visibilityState === 'visible');
}

export function watch(reference) {
    owner = reference;

    window.addEventListener('focus', report);
    window.addEventListener('blur', report);
    document.addEventListener('visibilitychange', report);

    report();
}

export function dispose() {
    window.removeEventListener('focus', report);
    window.removeEventListener('blur', report);
    document.removeEventListener('visibilitychange', report);

    owner = null;
}
