// Escape as "leave this page", listened for on the document because the key has to work wherever
// the focus happens to be — a text box, a dropdown, or nothing at all.
//
// Anything nearer than the page keeps the key: a Radzen popup or dialog closes on Escape too, and
// dismissing it is what the user meant. Radzen leaves a closed popup in the DOM with
// `display: none`, so an open one is the visible one — the same test Radzen's own handler makes.
//
// A page can name one more claimant with `ignore`: a region whose content owns the key whenever the
// focus is inside it. The terminal is the case that needs it — Escape typed at an agent is a message
// to the agent.

let owner = null;

let ignore = null;

let token = 0;

function popupOpen() {
    return [...document.querySelectorAll('.rz-popup,.rz-overlaypanel')]
        .some(popup => popup.style.display !== 'none');
}

function onKeyDown(event) {
    if (event.key !== 'Escape' && event.key !== 'Esc') {
        return;
    }

    if (event.defaultPrevented || event.repeat) {
        return;
    }

    if (popupOpen() || document.querySelector('.rz-dialog-content')) {
        return;
    }

    if (ignore && document.activeElement?.closest(ignore)) {
        return;
    }

    owner?.invokeMethodAsync('Escaped');
}

export function watch(reference, ignoreSelector) {
    owner = reference;
    ignore = ignoreSelector || null;
    token += 1;

    document.addEventListener('keydown', onKeyDown);

    return token;
}

// The token is what makes a late disposal harmless: navigating between two pages that both bind
// Escape can tear the old one down after the new one has already taken over, and the listener that
// then survives is the right one.
export function dispose(id) {
    if (id !== token) {
        return;
    }

    document.removeEventListener('keydown', onKeyDown);

    owner = null;
    ignore = null;
}
