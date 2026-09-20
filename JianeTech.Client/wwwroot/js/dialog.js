// ============================================================================
// dialog.js — the only JavaScript in the console.
//
// It exists because <dialog>.showModal() is the one thing Blazor cannot reach
// from C#, and showModal() is what buys the focus trap, the inert background
// and the top-layer rendering that a hand-rolled overlay only approximates.
//
// Three behaviours are enforced here, all of them deliberate:
//
//   1. Escape does not close the dialog. This takes two handlers, not one.
//      Preventing the `cancel` event is the documented way and it does work —
//      but only for the first press: Chromium's close watcher spends the user
//      activation on that refusal and force-closes on the next Escape, which
//      is deliberate anti-abuse behaviour, not a bug. Stopping the keydown
//      before it becomes a close request is what actually holds, because the
//      close signal is never generated. `cancel` stays as the second line, for
//      the close requests that do not come from the key — a platform back
//      gesture, for one.
//   2. Clicking the ground behind does not close it either — that is already
//      the default for showModal(), so there is simply no handler to add.
//   3. The page behind stops scrolling while a dialog is open. showModal()
//      makes the document inert to pointer and keyboard, but the wheel still
//      moves it, which reads as the dialog drifting.
// ============================================================================

let openCount = 0;

export function show(dialog) {
    if (!dialog || dialog.open) {
        return;
    }

    dialog.addEventListener('cancel', block);
    dialog.addEventListener('keydown', blockEscape, true);
    dialog.showModal();

    // showModal() honours [autofocus] only when nothing in the document is focused, so on
    // every open after the first the caret landed on the ✕ instead of the first field —
    // whatever opened the dialog still had focus. Placing it here makes the dialog open
    // the same way every time.
    dialog.querySelector('[autofocus]')?.focus();

    openCount += 1;
    document.body.style.overflow = 'hidden';
}

export function close(dialog) {
    if (!dialog) {
        return;
    }

    dialog.removeEventListener('cancel', block);
    dialog.removeEventListener('keydown', blockEscape, true);

    if (dialog.open) {
        dialog.close();
    }

    // Counted rather than cleared outright: closing one dialog must not hand
    // the page back its scrollbar while another is still open.
    openCount = Math.max(0, openCount - 1);
    if (openCount === 0) {
        document.body.style.overflow = '';
    }
}

function block(event) {
    event.preventDefault();
}

// Capture phase, so it runs before anything inside the dialog can act on the
// key and before the close watcher sees a close signal at all.
function blockEscape(event) {
    if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
    }
}

/**
 * Put the caret in the first field the form rejected.
 *
 * The dialog body scrolls, so a failure below the fold would otherwise be
 * reported somewhere the person cannot see. Blazor marks rejected inputs with
 * its own `invalid` class, which is the only thing here that knows which field
 * came first — the validation messages are siblings, not a list.
 */
export function focusFirstInvalid(dialog) {
    const field = dialog?.querySelector('.invalid');
    if (!field) {
        return;
    }

    field.focus({ preventScroll: true });
    field.scrollIntoView({ block: 'center', behavior: 'smooth' });
}
