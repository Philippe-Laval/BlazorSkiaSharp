using SkiaSharp;

namespace BlazorSkiaSharp.Components;

/// <summary>
/// Geometry for dragging the viewport frame on the minimap.
/// </summary>
/// <remarks>
/// A press inside the frame grabs it and moves it 1:1, keeping the offset it was grabbed
/// at. A press outside it centres the view there instead, which is the jump to a distant
/// part of the graph. Without that distinction, grabbing the frame snaps its centre under
/// the pointer, so the view lurches by half the frame before it starts following the drag.
/// <para>
/// Pure functions on purpose: this is the fiddly part of the gesture and it has to be
/// verifiable without a browser.
/// </para>
/// </remarks>
public static class MinimapFrameDrag
{
    /// <summary>
    /// How far outside the frame a press still counts as grabbing it, so a frame a pixel or
    /// two wide is still grabbable.
    /// </summary>
    public const float HitTolerance = 3f;

    /// <summary>
    /// How far the pointer may travel and still count as a click rather than a drag.
    /// </summary>
    public const float ClickSlop = 3f;

    /// <summary>Whether <paramref name="point"/> is on the frame, ignoring a degenerate one.</summary>
    public static bool Contains(SKRect frame, SKPoint point) =>
        frame.Width > 0f && frame.Height > 0f &&
        point.X >= frame.Left - HitTolerance && point.X <= frame.Right + HitTolerance &&
        point.Y >= frame.Top - HitTolerance && point.Y <= frame.Bottom + HitTolerance;

    /// <summary>
    /// Offset from the pointer to the frame centre, captured on press. It is zero when the
    /// press landed outside the frame, which turns the drag into a plain centre on the
    /// pointer.
    /// </summary>
    public static SKPoint GrabOffset(SKRect frame, SKPoint press)
    {
        if (!Contains(frame, press))
            return SKPoint.Empty;

        var center = new SKPoint(frame.Left + (frame.Width / 2f), frame.Top + (frame.Height / 2f));

        return new SKPoint(center.X - press.X, center.Y - press.Y);
    }

    /// <summary>Where the frame centre should go for this pointer position.</summary>
    public static SKPoint Target(SKPoint pointer, SKPoint grabOffset) =>
        new(pointer.X + grabOffset.X, pointer.Y + grabOffset.Y);

    /// <summary>Whether the gesture is still a click, i.e. the pointer has not really moved.</summary>
    public static bool IsClick(SKPoint press, SKPoint current) =>
        MathF.Abs(current.X - press.X) <= ClickSlop &&
        MathF.Abs(current.Y - press.Y) <= ClickSlop;
}