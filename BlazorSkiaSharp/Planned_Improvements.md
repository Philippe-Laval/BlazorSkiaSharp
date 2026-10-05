# Can you suggest functionality for minimap using best practices ?

Good question. Here's what I'd add, roughly ordered by value per effort. I checked the current code for each of these.

## Fixes first (two are real defects)

**1. The viewport frame is drawn outside the minimap's content rect.** `MinimapView.razor:177-179` — the graph is drawn inside `canvas.Save()`/`ClipRect(_content)`/`Restore()`, but `DrawViewportOverlay` runs *after* the restore, so the frame spills over the dark padding. At 100% zoom it covers the whole widget including the border area. Clip the overlay too, or at least inset it.

**2. Three `SKPaint` allocations per minimap frame** (`MinimapView.razor:134-135, 194`). Trivial to hoist to fields like `GraphRenderer` already does.

**3. The minimap re-renders the graph on every viewport change.** This is the big one. The graph part is completely invariant — only the frame moves. Record it once into an `SKPicture` (or an `SKImage` sized to the minimap) and blit it each frame, invalidating only when `Graph` or the minimap size changes. Wheel-zoom and drag currently pay for 210 edges + 80 dots per frame through the CPU raster + `putImageData` path.

**4. Clamp panning to the graph bounds (with slack).** Right now you can pan the graph completely off-screen and lose it with no way back but `Fit`. Soft clamping — allow ~15% of the viewport past the bounds, then resist — is what Figma/draw.io/Miro do and it prevents the most common "where did it go" moment.

## Navigation and interaction

**5. Drag the frame instead of always recentering.** Currently the minimap centers the viewport wherever you press. Best practice is: click jumps-to-center, but if you grab *inside the existing frame*, drag it 1:1. This needs a hit test plus a grab offset.

**6. Right-click zoom menu.** `Zoom to fit / Zoom to 100% / 50% / 200%`. Figma's mini-menu. Cheap to build from the existing `FitToContent`/`ResetView` methods.

**7. Hover crosshair in the main canvas.** Hovering the minimap draws a small crosshair where that point would land in the viewport. Makes the minimap feel connected to the canvas.

**8. Tooltip on hover.** "Click to jump · drag to pan · wheel to zoom". Pure discoverability — you currently only learn this by reading the paragraph above the canvas.

**9. Zoom percentage inside the minimap corner.** The minimap is the natural home for "how am I zoomed", and you need it right there while using it.

**10. `Ctrl/Cmd+0` reset, `Ctrl/Cmd+1` fit** alongside the bare digits. Bare `0`/`1` only work because the canvas is focused; the platform convention is the modifier.

## Layout and accessibility

**11. Collapse/expand toggle.** 260×170 is a real tax on small screens. This is the most universally expected minimap feature (Figma, VS Code, draw.io all have it) and it's the one thing here I'd call missing rather than optional.

**12. Resizable minimap** by dragging its corner, optionally remembering position/side.

**13. Make the minimap keyboard-operable.** It's currently mouse-only, so the one widget that gives global navigation is unreachable by keyboard. Focusable + arrow keys to pan is the fix.

## Bigger, only if you need them

- **Selection-aware minimap** — highlight selected nodes and draw the viewport frame around the selection when nothing is selected. Natural once you have a selection model; pointless before.
- **Inertial/rubber-band zoom** for trackpad and touch. Native-feeling but it's a gesture-state machine and a slippery slope.
- **Snap to 100%** when a zoom lands within a few percent of it.

One design cleanup worth doing regardless: `SourceSizeProvider` (`Func<SKSize>`) is a workaround for the minimap not knowing the main canvas size. Cleaner is to have the viewport hold its surface size via attach/detach, so the minimap just reads `Viewport.SurfaceSize`. That also makes items 3 and 5 easier.

Want me to implement a set? My suggestion would be **1–4 + 6 + 11**, which is a solid, coherent chunk — correctness and performance first, then the zoom menu and the toggle.