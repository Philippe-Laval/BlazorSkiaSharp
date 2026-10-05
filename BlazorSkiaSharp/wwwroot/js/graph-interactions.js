// Pointer, wheel and keyboard input for the graph canvas surfaces.
//
// The canvases are rendered by SKCanvasView, which only splats unmatched attributes onto
// its <canvas> element and cannot carry Blazor event callbacks, so the input is wired up
// here instead. That also buys the few things a canvas viewer really needs:
//
//   * wheel listeners are registered as non passive, so zooming never scrolls the page;
//   * moves are tracked on the window, so a drag keeps working past the canvas border;
//   * every finger is reported separately, which lets a second one turn the gesture
//     into a pinch;
//   * the original event object is available, so preventDefault and stopPropagation
//     behave exactly as needed for nested surfaces.

const surfaces = new Map();
let drag = null;
let dragPointerId = -1;

// Keys the surfaces handle; everything else is left to the browser so Tab keeps working.
const handledKeys = new Set(['+', '=', '-', '_', '0', '1', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']);

export function attachSurface(dotNetRef, element, stopPropagation, focusable) {
    detachSurface(element);

    const surface = { ref: dotNetRef, element, stopPropagation, focusable };

    if (focusable) {
        element.tabIndex = 0;
    }

    surfaces.set(element, surface);

    element.addEventListener('pointerdown', onPointerDown);
    element.addEventListener('wheel', onWheel, { passive: false });
    element.addEventListener('keydown', onKeyDown);
}

export function detachSurface(element) {
    const surface = surfaces.get(element);

    if (!surface)
        return;

    surfaces.delete(element);

    surface.element.removeEventListener('pointerdown', onPointerDown);
    surface.element.removeEventListener('wheel', onWheel);
    surface.element.removeEventListener('keydown', onKeyDown);

    // Drop the drag without notifying: .NET is going away and must not be called back.
    if (drag && drag.element === element)
        releaseDrag(false);
}

export function beginDrag(element, cursor) {
    const surface = surfaces.get(element);

    if (!surface)
        return;

    // Detach silently: .NET already knows about every pointer, because each one announced
    // itself with a pointerdown on the surface.
    detachWindowListeners();

    drag = surface;
    dragPointerId = -1;

    window.addEventListener('pointermove', onPointerMove, { passive: false });
    window.addEventListener('pointerup', onPointerUp);
    window.addEventListener('pointercancel', onPointerUp);
    window.addEventListener('blur', onPointerUp);

    document.body.style.cursor = cursor || 'grabbing';
    document.body.style.userSelect = 'none';
}

export function endDrag() {
    releaseDrag(true);
}

function releaseDrag(notify) {
    const surface = drag;
    const pointerId = dragPointerId;

    detachWindowListeners();

    drag = null;
    dragPointerId = -1;

    document.body.style.cursor = '';
    document.body.style.userSelect = '';

    if (surface && notify)
        surface.ref.invokeMethodAsync('OnPointerUp', pointerId);
}

function detachWindowListeners() {
    window.removeEventListener('pointermove', onPointerMove);
    window.removeEventListener('pointerup', onPointerUp);
    window.removeEventListener('pointercancel', onPointerUp);
    window.removeEventListener('blur', onPointerUp);
}

// Positions are measured against the surface box rather than taken from offsetX/offsetY,
// because offsetX is relative to the event target, which may be a child canvas.
function localPoint(element, event) {
    const box = element.getBoundingClientRect();
    return [event.clientX - box.left, event.clientY - box.top];
}

function onPointerDown(e) {
    const surface = surfaces.get(e.currentTarget);

    if (!surface)
        return;

    // Only the primary mouse button starts a gesture.
    if (e.pointerType === 'mouse' && e.button !== 0)
        return;

    e.preventDefault();

    if (surface.stopPropagation)
        e.stopPropagation();

    if (surface.focusable)
        surface.element.focus({ preventScroll: true });

    const [x, y] = localPoint(surface.element, e);

    surface.ref.invokeMethodAsync('OnPointerDown', x, y, e.clientX, e.clientY, e.pointerId);
}

function onPointerMove(e) {
    if (!drag)
        return;

    if (e.cancelable)
        e.preventDefault();

    dragPointerId = e.pointerId;

    const [x, y] = localPoint(drag.element, e);

    drag.ref.invokeMethodAsync('OnPointerMove', x, y, e.clientX, e.clientY, e.pointerId);
}

function onPointerUp(e) {
    dragPointerId = e.pointerId;
    endDrag();
}

function onWheel(e) {
    const surface = surfaces.get(e.currentTarget);

    if (!surface)
        return;

    // Stop the page from scrolling while the user zooms the graph.
    e.preventDefault();

    if (surface.stopPropagation)
        e.stopPropagation();

    const [x, y] = localPoint(surface.element, e);

    surface.ref.invokeMethodAsync('OnWheel', x, y, e.deltaY, e.deltaMode, e.ctrlKey);
}

function onKeyDown(e) {
    if (e.altKey || e.ctrlKey || e.metaKey || !handledKeys.has(e.key))
        return;

    const surface = surfaces.get(e.currentTarget);

    if (!surface)
        return;

    // Arrow keys would otherwise scroll the page behind the graph.
    e.preventDefault();

    if (surface.stopPropagation)
        e.stopPropagation();

    surface.ref.invokeMethodAsync('OnKeyDown', e.key);
}
