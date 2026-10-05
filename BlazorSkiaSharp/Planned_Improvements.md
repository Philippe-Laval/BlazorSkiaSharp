# Graph viewer — current state and planned improvements

Status of `/graph-drawing` (the `SKCanvasView` pan / zoom viewer with minimap).

## What exists today

| Area | Where |
| --- | --- |
| Shared pan / zoom transform | `Graphs/GraphViewport.cs` |
| Demo graph (80 nodes, 210 edges) | `Graphs/GraphModel.cs` |
| Graph drawing with culling + level of detail | `Graphs/GraphRenderer.cs` |
| Wheel / trackpad → zoom factor | `Graphs/ZoomMath.cs` |
| Pointer, wheel and keyboard input bridging | `Graphs/GraphSurfaceInput.cs`, `wwwroot/js/graph-interactions.js` |
| Minimap component | `Components/MinimapView.razor`, `.razor.css` |
| Minimap layout state and persistence | `Components/MinimapSettings.cs` |
| Page, toolbar, gestures | `Pages/GraphDrawing.razor` |

**Interaction shipped**

- Drag to pan, mouse wheel or trackpad pinch to zoom (anchored on the cursor).
- Two-finger pinch to zoom on touch; panning resumes with the remaining finger.
- Minimap: click or drag to move the viewport, wheel to zoom it around the cursor.
- Minimap: arrow keys pan, `+` / `-` zoom, and it is reachable by Tab.
- Minimap: collapsible, resizable by dragging its grip, anchored to any of the four
  corners; size, corner and visibility are remembered in `localStorage`.
- Toolbar: zoom in / out, reset, fit, minimap toggle and corner picker; live zoom readout.
- Keyboard on the focused canvas: `+` `-` zoom, `0` reset, `1` fit, arrows pan.

**Notable implementation decisions**

- `SKCanvasView` exposes no event parameters, so unmatched attributes cannot carry Blazor
  event callbacks. Input is therefore wired up in JS and reported through a
  `DotNetObjectReference`. That also buys non-passive wheel listeners (zooming never
  scrolls the page), window-level move tracking (a drag survives leaving the canvas) and
  one id per finger so a second finger can pinch.
- The minimap is a peer of the main canvas, not a child: both share one `GraphViewport`
  instance and repaint from its `Changed` event.
- The minimap caches its graph as an `SKPicture` and replays it, because the graph is
  invariant while only the viewport frame moves.

## Completed

### Minimap correctness and cost

1. **The viewport frame no longer paints outside the minimap.**
   `DrawViewportFrame` takes the intersection of the viewport rect with `_content`, rather
   than clipping. Clipping would slice the stroke in half at the boundary; intersecting
   draws the stroke whole, on the visible edge, and doubles as the "viewport covers
   everything" indicator when zoomed out. The graph drawing is still clipped to the
   content rect.
   Side effect: `ComputeMapping` was building `_content` with the 4-argument
   `SKRect.Create`, which is x/y/width/height, so the content rect had no right/bottom
   padding. Corrected to the `SKRect` constructor.

2. **`SKPaint` objects are now fields.** `_nodePaint`, `_linePaint` and `_framePaint` are
   allocated once and disposed with the component, like `GraphRenderer` already did.

3. **The minimised graph is recorded once into an `SKPicture` and replayed.**
   `EnsurePicture` re-records only when `Graph` or `_content` (the minimap size) changes;
   panning and zooming only replay it. Since the picture is only as good as its cache key,
   `Graph` is treated as immutable — true today, as `GraphModel` exposes only
   `init` properties and `IReadOnlyList`. If it ever becomes mutable, add explicit
   invalidation.

4. **Panning is clamped to the graph bounds, with 15% slack.** `GraphViewport.Bounds` and
   `GraphViewport.SlackRatio` drive it, and the page publishes `_graph.Bounds`. Only
   `PanByScreenDelta` is clamped: `CenterOn` and `ZoomAt` are deliberate user actions and
   are left alone, so the minimap can still centre on the very edge of the graph. Content
   smaller than the surface pans freely, since there is nothing to lose.

### Collapse / expand toggle

A `Minimap` toggle button in the toolbar, with `aria-pressed` reflecting the state.
Collapsing removes the minimap markup entirely, and `MinimapView` attaches and detaches
its input listeners from `OnAfterRenderAsync` as the markup comes and goes — the old
attach-once-in-`OnAfterRender(firstRender)` path would have left a collapsed minimap
listening to a detached element.

### Resizable, with a remembered position

- A grip in the corner diagonally opposite the anchored corner, drawn in CSS and hit
  tested against a 16px box plus a 6px tolerance for touch. Pressing inside the grip
  starts a resize instead of a viewport move.
- The anchored corner is pinned in *screen* coordinates on pointer down and everything is
  measured from it, so the minimap grows away from the corner it stays attached to. The
  box origin is derived from the pointer sample (which carries both client and
  surface-relative positions) rather than measured through JS.
- Clamped to 140×100 … 720×520.
- `MinimapCorner` is a parameter, set from a toolbar `<select>`, and both it and the
  size are persisted by `MinimapSettingsStore` (plain `localStorage`, no serializer).

### Keyboard-operable

The surface is `tabindex="0"` with `role="application"` and a focus ring. Arrow keys pan
the main viewport, `+` / `-` zoom it, matching the page's own shortcuts. The JS module
only calls `preventDefault` for the keys it actually handles, so Tab navigation and
unhandled keys behave normally.

### Surface size moved into the viewport

The minimap used to be handed a `Func<SKSize>` (`SourceSizeProvider`) purely because it
could not otherwise know the main canvas size. That delegate is gone. `GraphViewport` now
owns the surface it maps to:

- `SurfaceSize`, `HasSurface`, `SurfaceCenter`
- `AttachSurface(size)` — records the measured size on first paint
- `ResizeSurface(size)` — records a new size after a resize, keeping the world point that
  was at the centre of the old canvas at the centre of the new one

Every operation that used to take a size as a parameter lost it: `GetVisibleWorldRect()`,
`ZoomAroundWorld(anchor, factor)`, `CenterOn(center)`, `FitTo(bounds, padding)`, `Reset()`.

Knock-on cleanups:

- The page's resize branch no longer reaches into "the previous size" via a local
  snapshot — that policy lives in `ResizeSurface`.
- `_minimap?.Invalidate()` on resize is gone; `AttachSurface` raises `Changed`, which the
  minimap already subscribes to.
- `MinimapView.Invalidate()` and the `MinimapView @ref` existed only to plumb the size.
  Both removed.
- `_viewportSize` and `GetViewportSize()` removed from the page.

## Backlog

Priority is value per effort. Line references are from the current code.

### Navigation and interaction

1. **Drag the frame instead of always recentering.** Click should jump-to-centre, but a
   drag that starts *inside the existing frame* should move it 1:1. Needs a hit test plus
   a grab offset. Easier now that the viewport owns `SurfaceSize`.
2. **Right-click zoom menu.** `Zoom to fit / 100% / 50% / 200%`. Cheap to build from the
   existing `FitToContent` / `ResetView`.
3. **Hover crosshair in the main canvas**, showing where the hovered minimap point lands.
4. **Tooltip on hover.** "Click to jump · drag to pan · wheel to zoom". Currently the
   gesture set is only documented in the paragraph above the canvas.
5. **Zoom percentage inside the minimap corner**, where you need it while using it.
6. **`Ctrl`/`Cmd+0` reset and `Ctrl`/`Cmd+1` fit** alongside the bare digits. Bare digits
   only work because the canvas has focus; the modifier is the platform convention.

### Only if needed

- **Selection-aware minimap** — highlight selected nodes, frame the selection when
  nothing is selected. Natural once a selection model exists; pointless before.
- **Inertial / rubber-band zoom** for trackpad and touch. Native-feeling, but it is a
  gesture state machine and a slippery slope.
- **Snap to 100%** when a zoom lands within a few percent of it.

## Gotchas found while building this

Worth keeping, because every one was silent — wrong-looking output with no error.

1. **`SKRect.Create(x, y, width, height)` is x/y/width/height, not left/top/right/bottom.**
   Passing corners produced oversized rects. Use `new SKRect(l, t, r, b)`. This bit twice:
   once in `GraphModel`, then again in `MinimapView.ComputeMapping`, which had been giving
   the minimap's content rect no right/bottom padding.
2. **Canvas transforms pre-concatenate.** `Scale()` then `Translate()` moves by *world*
   units, not screen units. Translate first (`GraphViewport.ApplyTo`, `GraphViewport.cs:97`).
3. **SkiaSharp 4.x removed `SKPointF`, `SKRectF`, `SKSizeF`.** `SKPoint`/`SKSize`/`SKRect`
   are float-based now. `SKPaint.TextAlign` is also gone; pass alignment to
   `DrawText(...)`. `SKPath.MoveTo`/`LineTo` are obsolete in favour of `SKPathBuilder`.
4. **Pan clamping has to work from the bounds' edges, not its size.** The first attempt
   clamped against `bounds.Width * scale`, which is only correct when the bounds start at
   the world origin. The demo graph's bounds are inset by a margin (`Left`/`Top` are
   negative), so the limit was off by that margin and the graph could still be dragged out
   of sight. Covered by the harness now.
5. **Grab-and-drag means the content follows the pointer.** Panning with
   `translation += pointerDelta` is correct and looks like the drawing is being dragged;
   the intuitive "inverted" reading is wrong. Worth stating because it reads as a bug.
   Arrow keys are the opposite case: `ArrowRight` moves the *view* right, so the content
   moves left.
6. **Blazor renders a `bool` attribute as an HTML boolean attribute.** Writing
   `aria-pressed="@someBool"` produces `aria-pressed=""` when true and omits it when
   false, which is wrong for ARIA. Cast to the strings `"true"` / `"false"` instead.
7. **Gesture-end callbacks need to live with the gesture.** Persisting the minimap size on
   the page's pointer-up never fired, because a minimap resize is tracked by the
   minimap's own input, not the page's. `MinimapView.ResizeEnded` now signals it.

## Verification status

- Build clean, no warnings.
- 96 assertions in the throwaway console harness
  (`%TEMP%\opencode\viewport-check`) against the real `GraphViewport` and
  `MinimapSettings` sources. All pass. Worth promoting into the repo as a real test
  project — it is what caught the clamp and the storage-parsing bugs.
- Verified in the browser on the current build, measuring rendered pixels and reading the
  DOM:
  - initial fit is pixel-centred, and still is after the minimap changes;
  - the minimap viewport frame stays inside the content rect at every zoom;
  - the recorded minimap picture keeps rendering correctly across zoom, pan and minimap
    interaction;
  - at 40% zoom, dragging hard in both directions always leaves the graph visible;
  - collapse hides the minimap and restores it at the same size;
  - all four corners position correctly (13px inset from the two anchored edges), and the
    grip always sits diagonally opposite with the matching resize cursor;
  - resize drag changes the size, does not move the viewport, preserves the anchor, and
    clamps at 720×520;
  - the minimap is focusable; arrow keys pan the main viewport by 48px, `+`/`-` zoom it,
    and unhandled keys are left alone;
  - size, corner and collapsed state all survive a reload through `localStorage`;
  - no console errors.
