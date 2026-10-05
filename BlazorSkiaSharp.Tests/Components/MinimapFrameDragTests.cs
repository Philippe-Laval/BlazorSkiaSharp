using BlazorSkiaSharp.Components;
using SkiaSharp;

namespace BlazorSkiaSharp.Tests.Components;

/// <summary>
/// Covers <see cref="MinimapFrameDrag"/>, the gesture that makes a press on the viewport
/// frame grab it and move it 1:1, while a press outside it still jumps to that point.
/// </summary>
/// <remarks>
/// The regression this guards against is subtle: if the offset is dropped, dragging still
/// moves the view, it just lurches by half the frame on the first move, which reads as the
/// frame being sticky rather than grabbed.
/// </remarks>
[TestClass]
public sealed class MinimapFrameDragTests
{
    private static readonly SKRect Frame = new(40f, 30f, 140f, 90f);

    private static SKPoint CenterOf(SKRect rect) =>
        new(rect.Left + (rect.Width / 2f), rect.Top + (rect.Height / 2f));

    private static void AssertClose(double expected, double actual, string because) =>
        Assert.AreEqual(expected, actual, 0.0001, because);

    // ----------------------------------------------------------------- hit test

    [TestMethod]
    public void FindsAPressInsideTheFrame()
    {
        Assert.IsTrue(MinimapFrameDrag.Contains(Frame, new SKPoint(90f, 60f)));
        Assert.IsTrue(MinimapFrameDrag.Contains(Frame, new SKPoint(Frame.Left, Frame.Top)));
        Assert.IsTrue(MinimapFrameDrag.Contains(Frame, new SKPoint(Frame.Right, Frame.Bottom)));
    }

    [TestMethod]
    [DataRow(-20f, 60f)]
    [DataRow(200f, 60f)]
    [DataRow(90f, -20f)]
    [DataRow(90f, 200f)]
    public void IgnoresAPressOutsideTheFrame(float x, float y)
    {
        Assert.IsFalse(MinimapFrameDrag.Contains(Frame, new SKPoint(x, y)));
    }

    [TestMethod]
    public void ToleratesAPressJustOutsideTheEdge()
    {
        // A frame a pixel or two wide would otherwise be impossible to grab.
        Assert.IsTrue(MinimapFrameDrag.Contains(Frame, new SKPoint(Frame.Left - 1f, Frame.Top - 1f)));
        Assert.IsTrue(MinimapFrameDrag.Contains(Frame, new SKPoint(Frame.Right + 1f, Frame.Bottom + 1f)));
        Assert.IsFalse(MinimapFrameDrag.Contains(Frame, new SKPoint(Frame.Left - 20f, 60f)));
    }

    [TestMethod]
    [DataRow(0f, 0f, 0f, 0f)]
    [DataRow(10f, 10f, 10f, 10f)]
    public void AnEmptyFrameIsNotGrabbable(float left, float top, float right, float bottom)
    {
        // SKRect.Empty is all zeroes and would otherwise report as containing the origin.
        var degenerate = new SKRect(left, top, right, bottom);

        Assert.IsFalse(MinimapFrameDrag.Contains(degenerate, new SKPoint(0f, 0f)));
        Assert.AreEqual(SKPoint.Empty, MinimapFrameDrag.GrabOffset(degenerate, new SKPoint(0f, 0f)));
    }

    // -------------------------------------------------------------- grab offset

    [TestMethod]
    public void GrabbingKeepsTheOffsetToTheFrameCentre()
    {
        var press = new SKPoint(Frame.Left + 5f, Frame.Top + 5f);

        var offset = MinimapFrameDrag.GrabOffset(Frame, press);

        var center = CenterOf(Frame);
        AssertClose(center.X - press.X, offset.X, "X offset");
        AssertClose(center.Y - press.Y, offset.Y, "Y offset");
    }

    [TestMethod]
    public void PressingTheCentreHasNoOffset()
    {
        var center = CenterOf(Frame);

        Assert.AreEqual(SKPoint.Empty, MinimapFrameDrag.GrabOffset(Frame, center));
    }

    [TestMethod]
    public void PressingOutsideTheFrameGrabsNothing()
    {
        Assert.AreEqual(SKPoint.Empty, MinimapFrameDrag.GrabOffset(Frame, new SKPoint(400f, 400f)));
    }

    // ------------------------------------------------------------------- moving

    [TestMethod]
    public void TheFrameFollowsThePointerOneToOne()
    {
        // The whole point of the grab offset: the frame centre tracks the pointer delta
        // exactly, instead of snapping its centre under the pointer.
        var press = new SKPoint(Frame.Left + 5f, Frame.Top + 5f);
        var offset = MinimapFrameDrag.GrabOffset(Frame, press);
        var before = MinimapFrameDrag.Target(press, offset);

        var after = MinimapFrameDrag.Target(new SKPoint(press.X + 37f, press.Y - 12f), offset);

        AssertClose(37f, after.X - before.X, "moved by the pointer delta on X");
        AssertClose(-12f, after.Y - before.Y, "moved by the pointer delta on Y");
    }

    [TestMethod]
    public void ThePressItselfDoesNotMoveAGrabbedFrame()
    {
        var press = new SKPoint(Frame.Left + 5f, Frame.Top + 5f);

        var target = MinimapFrameDrag.Target(press, MinimapFrameDrag.GrabOffset(Frame, press));

        var center = CenterOf(Frame);
        AssertClose(center.X, target.X, "the frame stays where it was on X");
        AssertClose(center.Y, target.Y, "the frame stays where it was on Y");
    }

    [TestMethod]
    public void APressOutsideTheFrameJumpsToThatPoint()
    {
        var press = new SKPoint(400f, 300f);

        var target = MinimapFrameDrag.Target(press, MinimapFrameDrag.GrabOffset(Frame, press));

        Assert.AreEqual(press, target);
    }

    // -------------------------------------------------------------- click versus drag

    [TestMethod]
    public void AStationaryPressIsAClick()
    {
        var press = new SKPoint(90f, 60f);

        Assert.IsTrue(MinimapFrameDrag.IsClick(press, press));
        Assert.IsTrue(MinimapFrameDrag.IsClick(press, new SKPoint(press.X + 1f, press.Y - 1f)));
    }

    [TestMethod]
    public void TravellingPastTheSlopIsADrag()
    {
        var press = new SKPoint(90f, 60f);

        Assert.IsFalse(MinimapFrameDrag.IsClick(press, new SKPoint(press.X + 40f, press.Y)));
        Assert.IsFalse(MinimapFrameDrag.IsClick(press, new SKPoint(press.X, press.Y - 40f)));
    }
}