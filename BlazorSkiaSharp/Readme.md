`GraphDrawing.razor` now has full pan/zoom with a minimap.

## What was added

**`Graphs/GraphViewport.cs`** — the shared pan/zoom transform (`screen = world * Scale + Translation`). Both canvases use the same instance, so a change on either is reflected on the other via a `Changed` event. Provides `ZoomAt` (anchors the point under the cursor), `ZoomAroundWorld`, `PanByScreenDelta`, `CenterOn`, `FitTo`, `Reset`, `GetVisibleWorldRect`.

**`Graphs/GraphModel.cs`** — a deterministic 80-node / 210-edge layered graph in world coordinates (6008 × 4328 units), far bigger than a window so panning/zooming matters.

**`Graphs/GraphRenderer.cs`** — draws the graph in world coordinates. Reuses Skia objects across frames, culls to the visible rect, drops labels and clamps stroke widths so zooming out stays cheap, and draws a grid with a 1/2/5/10 "nice" step.

**`Graphs/ZoomMath.cs`** — wheel-delta → zoom-factor conversion (normalises line/page delta modes; a stronger exponent for ctrl+wheel trackpad pinch).

**`Graphs/GraphSurfaceInput.cs`** + **`wwwroot/js/graph-interactions.js`** — input plumbing. `SKCanvasView` has no event parameters, so `@onclick`-style attributes can't carry Blazor callbacks onto its canvas. A small JS module wires up `pointerdown`/`wheel`/`keydown` and reports through `DotNetObjectReference`, which also buys non-passive wheel listeners (page never scrolls), window-level move tracking (a drag survives leaving the canvas) and one id per finger so a second finger turns the drag into a pinch.

**`Components/MinimapView.razor`** — reusable minimap: the whole graph scaled to fit, a translucent frame showing the current viewport, drag/click to move the viewport, wheel to zoom it.

## Two SkiaSharp 4.x API traps hit along the way

Both were caught by measuring the rendered pixels rather than trusting the math:

1. **`SKRect.Create(x, y, width, height)`** — not `left/top/right/bottom`. Using it as LTRB silently produced oversized rects. The constructor `new SKRect(l, t, r, b)` is the correct one.
2. **Canvas transforms pre-concatenate**, so `Scale()` then `Translate()` moves by *world* units. `ApplyTo` now translates first.

Also fixed: the resize handler was computing the preserved centre from the *new* surface size instead of the old one, so window resizes drifted the view.

## Verified in the browser

Fit is pixel-centred (margins 96/96/76/76); drag pan is pixel-exact on both axes; wheel zoom anchors correctly; a 3× two-finger spread takes 12% → 36%; minimap click/wheel and the `+ - 0 1`/arrow shortcuts all work; no console errors; build is warning-free.