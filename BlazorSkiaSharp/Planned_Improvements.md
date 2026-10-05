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

## Completed

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

### Fixes

1. **The viewport frame is drawn outside the minimap's content rect.**
   `Components/MinimapView.razor:169` — the graph is drawn inside
   `Save()` / `ClipRect(_content)` / `Restore()`, but `DrawViewportOverlay` (line 172)
   runs *after* the restore, so the frame spills over the dark padding and covers the
   whole widget at 100% zoom. Clip the overlay too, or inset it.
2. **Three `SKPaint` allocations per minimap frame.**
   `MinimapView.razor:125`, `:126`, `:182`. Hoist to fields, as `GraphRenderer` already does.
3. **The minimap re-renders the graph on every viewport change.** The big one: the graph
   part is invariant, only the frame moves. Record it once into an `SKPicture` (or an
   `SKImage` sized to the minimap) and blit it, invalidating only when `Graph` or the
   minimap size changes. Wheel-zoom and drag currently pay for 210 edges + 80 dots per
   frame through the CPU raster + `putImageData` path.
4. **Clamp panning to the graph bounds, with slack.** The graph can currently be panned
   completely off-screen with no way back but `Fit`. Allow ~15% of the viewport past the
   bounds, then resist, as Figma / draw.io / Miro do.

### Navigation and interaction

5. **Drag the frame instead of always recentering.** Click should jump-to-centre, but a
   drag that starts *inside the existing frame* should move it 1:1. Needs a hit test plus
   a grab offset. Easier now that the viewport owns `SurfaceSize`.
6. **Right-click zoom menu.** `Zoom to fit / 100% / 50% / 200%`. Cheap to build from the
   existing `FitToContent` / `ResetView`.
7. **Hover crosshair in the main canvas**, showing where the hovered minimap point lands.
8. **Tooltip on hover.** "Click to jump · drag to pan · wheel to zoom". Currently the
   gesture set is only documented in the paragraph above the canvas.
9. **Zoom percentage inside the minimap corner**, where you need it while using it.
10. **`Ctrl`/`Cmd+0` reset and `Ctrl`/`Cmd+1` fit** alongside the bare digits. Bare digits
    only work because the canvas has focus; the modifier is the platform convention.

### Layout and accessibility

11. **Collapse / expand toggle.** 260×170 is a real tax on small screens, and a toggle is
    the most universally expected minimap feature (Figma, VS Code, draw.io). The one item
    here that reads as missing rather than optional.
12. **Resizable minimap**, optionally remembering position and side.
13. **Keyboard-operable minimap.** It is mouse-only today, so the one widget that gives
    global navigation is unreachable by keyboard. Focusable, arrows to pan.

### Only if needed

- **Selection-aware minimap** — highlight selected nodes, frame the selection when
  nothing is selected. Natural once a selection model exists; pointless before.
- **Inertial / rubber-band zoom** for trackpad and touch. Native-feeling, but it is a
  gesture state machine and a slippery slope.
- **Snap to 100%** when a zoom lands within a few percent of it.

## Gotchas found while building this

Worth keeping, because all three were silent — wrong-looking output with no error.

1. **`SKRect.Create(x, y, width, height)` is x/y/width/height, not left/top/right/bottom.**
   Passing corners produced oversized rects. Use `new SKRect(l, t, r, b)`.
2. **Canvas transforms pre-concatenate.** `Scale()` then `Translate()` moves by *world*
   units, not screen units. Translate first (`GraphViewport.ApplyTo`, `GraphViewport.cs:97`).
3. **SkiaSharp 4.x removed `SKPointF`, `SKRectF`, `SKSizeF`.** `SKPoint`/`SKSize`/`SKRect`
   are float-based now. `SKPaint.TextAlign` is also gone; pass alignment to
   `DrawText(...)`. `SKPath.MoveTo`/`LineTo` are obsolete in favour of `SKPathBuilder`.

## Verification status

- Build clean, no warnings.
- `GraphViewport` logic covered by a 47-assertion console harness
  (`%TEMP%\opencode\viewport-check`, throwaway). All pass. It is worth promoting into the
  repo as a real test project.
- DOM and JS wiring confirmed in-browser: both canvases created, `tabIndex` set on the
  stage by `attachSurface`, no console errors.
- **Pixel-level behaviour is currently unverified on the latest build.** The browser window
  went hidden, which throttles `requestAnimationFrame`, so `SKCanvasView` never paints and
  canvases stay transparent. Fit centring, exact pan deltas and the minimap frame were last
  confirmed against the pre-refactor build and need re-running once the window is visible.
