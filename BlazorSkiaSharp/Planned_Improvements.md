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
| Minimap component | `Components/MinimapView.razor` |
| Page, toolbar, gestures | `Pages/GraphDrawing.razor` |

**Interaction shipped**

- Drag to pan, mouse wheel or trackpad pinch to zoom (anchored on the cursor).
- Two-finger pinch to zoom on touch; panning resumes with the remaining finger.
- Minimap: click or drag to move the viewport, wheel to zoom it around the cursor.
- Toolbar: zoom in / out, reset, fit; live zoom readout.
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

### Layout and accessibility

7. **Collapse / expand toggle.** 260×170 is a real tax on small screens, and a toggle is
   the most universally expected minimap feature (Figma, VS Code, draw.io). The one item
   here that reads as missing rather than optional.
8. **Resizable minimap**, optionally remembering position and side.
9. **Keyboard-operable minimap.** It is mouse-only today, so the one widget that gives
   global navigation is unreachable by keyboard. Focusable, arrows to pan.

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

## Verification status

- Build clean, no warnings.
- `GraphViewport` logic covered by an 81-assertion console harness
  (`%TEMP%\opencode\viewport-check`, throwaway). All pass. Worth promoting into the repo
  as a real test project — it is what caught gotcha 4.
- Verified in the browser on the current build, measuring rendered pixels:
  - initial fit is pixel-centred (margins 82/82/106/106 on a 738×612 canvas);
  - grab-and-drag pan is 1:1 with the pointer (step by step: -20 px of pointer per -20 px
    of content);
  - the minimap viewport frame stays inside the content rect at every zoom
    (`[7,252,7,162]` against a content rect of `7..253 / 7..163`);
  - the recorded minimap picture keeps rendering correctly across zoom, pan and minimap
    interaction;
  - at 40% zoom, dragging hard in both directions always leaves the graph visible;
  - minimap click and wheel still move and zoom the viewport; no console errors.
- **Not measured:** the frame-time improvement from the picture cache. Reuse is guaranteed
  by the guard in `EnsurePicture`, and correctness is verified, but the speed-up itself was
  not benchmarked.
