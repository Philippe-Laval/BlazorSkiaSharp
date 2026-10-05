using SkiaSharp;

namespace BlazorSkiaSharp.Graphs;

/// <summary>
/// Pan / zoom state of a drawing surface, stored as a single uniform transform:
/// <c>screen = world * Scale + Translation</c>.
/// </summary>
/// <remarks>
/// The main canvas and the minimap share the same instance, so a change made on
/// either surface is immediately reflected on both through <see cref="Changed"/>.
/// </remarks>
public sealed class GraphViewport
{
    public const float MinScale = 0.05f;
    public const float MaxScale = 8f;

    private float scale = 1f;
    private SKPoint translation = SKPoint.Empty;

    /// <summary>Size in canvas pixels of the surface this viewport is mapped to.</summary>
    /// <remarks>
    /// The viewport is told about its surface once, when the surface is first measured,
    /// and the size is updated on resize. Anything that has to convert between screen
    /// and world coordinates reads this instead of being handed a size of its own, which
    /// is what lets the minimap share the very same viewport without extra plumbing.
    /// </remarks>
    public SKSize SurfaceSize { get; private set; }

    /// <summary>Whether a valid surface size has been supplied yet.</summary>
    public bool HasSurface => SurfaceSize.Width > 0f && SurfaceSize.Height > 0f;

    /// <summary>Centre of <see cref="SurfaceSize"/>.</summary>
    public SKPoint SurfaceCenter => new(SurfaceSize.Width / 2f, SurfaceSize.Height / 2f);

    /// <summary>Raised whenever the transform changed and both surfaces should repaint.</summary>
    public event Action? Changed;

    /// <summary>Current zoom factor (1 = 100%).</summary>
    public float Scale => scale;

    /// <summary>Screen space offset, in canvas pixels, applied after scaling.</summary>
    public SKPoint Translation => translation;

    /// <summary>
    /// Records the measured size of the surface this viewport drives. Used on the first
    /// paint, when there is no previous surface to preserve anything about.
    /// </summary>
    public void AttachSurface(SKSize size) => SetSurfaceSize(size);

    /// <summary>
    /// Records a new size for the surface after a resize, keeping the world point that
    /// was in the middle of the old canvas in the middle of the new one.
    /// </summary>
    public void ResizeSurface(SKSize size)
    {
        if (!HasSurface)
        {
            AttachSurface(size);
            return;
        }

        var center = ScreenToWorld(SurfaceCenter);

        SetSurfaceSize(size);
        CenterOn(center);
    }

    private void SetSurfaceSize(SKSize size)
    {
        // Only a real change is worth a repaint; every paint of the same size calls this.
        if (SurfaceSize == size)
            return;

        SurfaceSize = size;
        NotifyChanged();
    }

    /// <summary>Maps a point from world (graph) coordinates to canvas pixels.</summary>
    public SKPoint WorldToScreen(SKPoint world) => new(
        (world.X * scale) + translation.X,
        (world.Y * scale) + translation.Y);

    /// <summary>Maps a point from canvas pixels to world (graph) coordinates.</summary>
    public SKPoint ScreenToWorld(SKPoint screen) => new(
        (screen.X - translation.X) / scale,
        (screen.Y - translation.Y) / scale);

    /// <summary>
    /// Applies the transform to a canvas. The caller owns the matching
    /// <see cref="SKCanvas.Restore"/> (use together with <see cref="SKCanvas.Save"/>).
    /// </summary>
    /// <remarks>
    /// The order matters: canvas transforms pre-concatenate, so translating after scaling
    /// would move by world units. Translating first gives <c>screen = world * Scale + Translation</c>.
    /// </remarks>
    public void ApplyTo(SKCanvas canvas)
    {
        canvas.Translate(translation.X, translation.Y);
        canvas.Scale(scale);
    }

    /// <summary>World rectangle currently covered by <see cref="SurfaceSize"/>.</summary>
    public SKRect GetVisibleWorldRect()
    {
        var topLeft = ScreenToWorld(SKPoint.Empty);
        var bottomRight = ScreenToWorld(new SKPoint(SurfaceSize.Width, SurfaceSize.Height));

        return new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    /// <summary>
    /// Multiplies the zoom factor while keeping <paramref name="screenAnchor"/> over the
    /// same world point, which is what makes wheel zooming feel natural.
    /// </summary>
    public void ZoomAt(SKPoint screenAnchor, float factor)
    {
        if (factor <= 0f || float.IsNaN(factor) || float.IsInfinity(factor))
            return;

        var worldAnchor = ScreenToWorld(screenAnchor);
        var zoomed = Math.Clamp(scale * factor, MinScale, MaxScale);

        if (MathF.Abs(zoomed - scale) < 0.0001f)
            return;

        scale = zoomed;
        translation = new SKPoint(
            screenAnchor.X - (worldAnchor.X * scale),
            screenAnchor.Y - (worldAnchor.Y * scale));

        NotifyChanged();
    }

    /// <summary>
    /// Zooms while keeping <paramref name="worldAnchor"/> in place. When the anchor is
    /// outside the surface it is first pulled back to the closest visible point, which is
    /// what the minimap needs when the wheel is used far from the centre.
    /// </summary>
    public void ZoomAroundWorld(SKPoint worldAnchor, float factor)
    {
        if (!HasSurface)
            return;

        var anchor = WorldToScreen(worldAnchor);
        ZoomAt(new SKPoint(
            Math.Clamp(anchor.X, 0f, SurfaceSize.Width),
            Math.Clamp(anchor.Y, 0f, SurfaceSize.Height)), factor);
    }

    /// <summary>Scrolls the content by a canvas pixel delta.</summary>
    public void PanByScreenDelta(float dx, float dy)
    {
        if (dx == 0f && dy == 0f)
            return;

        translation = new SKPoint(translation.X + dx, translation.Y + dy);
        NotifyChanged();
    }

    /// <summary>Moves the transform so that <paramref name="worldCenter"/> sits at the surface centre.</summary>
    public void CenterOn(SKPoint worldCenter)
    {
        var center = SurfaceCenter;
        translation = new SKPoint(
            center.X - (worldCenter.X * scale),
            center.Y - (worldCenter.Y * scale));

        NotifyChanged();
    }

    /// <summary>Scales and translates so that <paramref name="worldBounds"/> fills the surface.</summary>
    public void FitTo(SKRect worldBounds, float paddingRatio = 0.08f)
    {
        if (!HasSurface)
            return;

        paddingRatio = Math.Clamp(paddingRatio, 0f, 0.45f);

        // The graph is centred on the surface, so the padding shrinks it on both sides.
        var availableWidth = SurfaceSize.Width * (1f - (2f * paddingRatio));
        var availableHeight = SurfaceSize.Height * (1f - (2f * paddingRatio));

        var sx = worldBounds.Width > 0f ? availableWidth / worldBounds.Width : 1f;
        var sy = worldBounds.Height > 0f ? availableHeight / worldBounds.Height : 1f;
        var fit = MathF.Min(sx, sy);

        if (float.IsNaN(fit) || float.IsInfinity(fit) || fit <= 0f)
            fit = 1f;

        scale = Math.Clamp(fit, MinScale, MaxScale);
        CenterOn(new SKPoint(
            worldBounds.Left + (worldBounds.Width / 2f),
            worldBounds.Top + (worldBounds.Height / 2f)));
    }

    /// <summary>Restores 1:1 scale with the world origin in the middle of the surface.</summary>
    public void Reset()
    {
        scale = 1f;
        CenterOn(SKPoint.Empty);
    }

    private void NotifyChanged() => Changed?.Invoke();
}
