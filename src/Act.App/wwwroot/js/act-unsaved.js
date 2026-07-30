// Guards the one exit Blazor cannot see: the window's own close button.
//
// The two hosts want opposite things here. A browser wants `preventDefault` on `beforeunload` and
// shows its own "leave site?" prompt. Electron ignores that prompt entirely — returning any value
// *silently* cancels the close — so on the desktop the page cancels the close itself and asks the
// app to put its own dialog up, then closes for real once the answer comes back.

let owner = null;

let desktop = false;

let dirty = false;

let leaving = false;

function onBeforeUnload(event) {
    if (!dirty || leaving) {
        return;
    }

    event.preventDefault();
    event.returnValue = '';

    if (desktop) {
        owner?.invokeMethodAsync('OnCloseBlocked');
    }
}

export function watch(reference, isDesktop) {
    owner = reference;
    desktop = isDesktop;

    window.addEventListener('beforeunload', onBeforeUnload);
}

export function arm(hasChanges) {
    dirty = hasChanges;
}

// Desktop only: the close was cancelled to make room for the dialog, so closing again is what
// carries out the answer. The flag is what stops the guard catching its own second attempt.
export function release() {
    leaving = true;

    window.close();
}

export function dispose() {
    window.removeEventListener('beforeunload', onBeforeUnload);

    owner = null;
    dirty = false;
    leaving = false;
}
