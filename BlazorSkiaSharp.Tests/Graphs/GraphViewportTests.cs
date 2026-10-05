using BlazorSkiaSharp.Graphs;
using SkiaSharp;

namespace BlazorSkiaSharp.Tests.Graphs;

/// <summary>
/// Covers <see cref="GraphViewport"/>: the pan / zoom transform, how it behaves before a
/// surface has been measured, and the clamping that keeps the graph reachable.
/// </summary>
[TestClass]
public sealed class GraphViewportTests
{
    private const float DefaultSlackRatio = 0.15f;

    private static readonly SKSize Surface = new(800f, 600f);

    /// <summary>The real demo graph, so the expectations stay honest if the model changes.</summary>
    private static readonly SKRect World = GraphModel.CreateDemo().Bounds;

    /// <summary>Square and large, so it overflows the surface on both axes once zoomed in.</summary>
    private static readonly SKRect BigSquare = new(0, 0, 6000, 6000);

    /// <summary>Wide but shallow, so only one axis overflows.</summary>
    private static readonly SKRect Wide = new(0, 0, 6000, 100);

    private static float Slack(float surfaceExtent) => surfaceExtent * DefaultSlackRatio;

    private static void AssertClose(double expected, double actual, string because, double tolerance = 0.01)
        => Assert.AreEqual(expected, actual, tolerance, because);

    /// <summary>Attaches a surface and zooms in until the content overflows it.</summary>
    private static GraphViewport ZoomedInto(SKRect bounds, float zoom, float slackRatio = DefaultSlackRatio)
    {
        var viewport = new GraphViewport { Bounds = bounds, SlackRatio = slackRatio };
        viewport.AttachSurface(Surface);
        viewport.FitTo(bounds);
        viewport.ZoomAt(viewport.SurfaceCenter, zoom);
        return viewport;
    }

    /// <summary>How much of <paramref name="bounds"/> overlaps the surface, in pixels.</summary>
    private static float OverlapOnScreen(GraphViewport viewport, SKRect bounds)
    {
        var visible = viewport.GetVisibleWorldRect();
        var overlap = Math.Min(visible.Right, bounds.Right) - Math.Max(visible.Left, bounds.Left);
        return overlap * viewport.Scale;
    }

    // ------------------------------------------------------------ detached state

    [TestMethod]
    public void StartsDetached()
    {
        var viewport = new GraphViewport();

        Assert.IsFalse(viewport.HasSurface);
    }

    [TestMethod]
    public void SurfaceCenterIsTheOriginWhileDetached()
    {
        var viewport = new GraphViewport();

        Assert.AreEqual(0f, viewport.SurfaceCenter.X);
        Assert.AreEqual(0f, viewport.SurfaceCenter.Y);
    }

    [TestMethod]
    public void FitToIsANoOpWhileDetached()
    {
        var viewport = new GraphViewport();
        var changed = 0;
        viewport.Changed += () => changed++;

        viewport.FitTo(World);

        Assert.AreEqual(1f, viewport.Scale);
        Assert.AreEqual(0, changed);
    }

    [TestMethod]
    public void ZoomAroundWorldIsANoOpWhileDetached()
    {
        var viewport = new GraphViewport();

        viewport.ZoomAroundWorld(SKPoint.Empty, 2f);

        Assert.AreEqual(1f, viewport.Scale);
    }

    // -------------------------------------------------------------------- attach

    [TestMethod]
    public void AttachSurfaceRaisesChanged()
    {
        var viewport = new GraphViewport();
        var changed = 0;
        viewport.Changed += () => changed++;

        viewport.AttachSurface(Surface);

        Assert.AreEqual(1, changed);
        Assert.IsTrue(viewport.HasSurface);
    }

    [TestMethod]
    public void AttachSurfaceRecordsSizeAndCentre()
    {
        var viewport = new GraphViewport();

        viewport.AttachSurface(Surface);

        Assert.AreEqual(Surface.Width, viewport.SurfaceSize.Width);
        Assert.AreEqual(Surface.Height, viewport.SurfaceSize.Height);
        Assert.AreEqual(Surface.Width / 2f, viewport.SurfaceCenter.X);
        Assert.AreEqual(Surface.Height / 2f, viewport.SurfaceCenter.Y);
    }

    [TestMethod]
    public void AttachingTheSameSizeDoesNotRaiseChanged()
    {
        var viewport = new GraphViewport();
        var changed = 0;
        viewport.Changed += () => changed++;

        viewport.AttachSurface(Surface);
        viewport.AttachSurface(Surface);

        Assert.AreEqual(1, changed, "every paint calls this, so a repeat must be free");
    }

    // ----------------------------------------------------------------------- fit

    [TestMethod]
    public void FitToUsesTheLimitingAxis()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.FitTo(World);

        var expected = Math.Min(Surface.Width * 0.84f / World.Width, Surface.Height * 0.84f / World.Height);
        AssertClose(expected, viewport.Scale, "scale should come from the tighter axis");
    }

    [TestMethod]
    public void FitToCentresTheGraph()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.FitTo(World);

        var center = viewport.ScreenToWorld(viewport.SurfaceCenter);
        AssertClose(World.Left + (World.Width / 2f), center.X, "world X centred", 0.05);
        AssertClose(World.Top + (World.Height / 2f), center.Y, "world Y centred", 0.05);
    }

    [TestMethod]
    public void FitToIsSymmetric()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.FitTo(World);

        var visible = viewport.GetVisibleWorldRect();
        AssertClose(visible.Left - World.Left, World.Right - visible.Right, "same slack left and right", 0.05);
        AssertClose(visible.Top - World.Top, World.Bottom - visible.Bottom, "same slack top and bottom", 0.05);
    }

    [TestMethod]
    public void FitToLeavesThePaddingOnTheAxisThatDecidedTheScale()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.FitTo(World);

        var visible = viewport.GetVisibleWorldRect();

        // Whichever axis produced the smaller fit scale is the one the padding applies to.
        // Note this cannot be asked as "did the content overflow": a fit always leaves the
        // content inside the padded box, so the answer is always no.
        var limitingIsX = (World.Width / Surface.Width) > (World.Height / Surface.Height);

        var padding = limitingIsX
            ? (World.Left - visible.Left) * viewport.Scale
            : (World.Top - visible.Top) * viewport.Scale;

        var surfaceExtent = limitingIsX ? Surface.Width : Surface.Height;

        AssertClose(0.08 * surfaceExtent, padding, "the padding is exact on the axis that set the scale", 0.5);
    }

    [TestMethod]
    public void FitToLeavesAtLeastThePaddingOnBothAxes()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.FitTo(World);

        var visible = viewport.GetVisibleWorldRect();

        // NB: MSTest's comparison asserts take the bound first, then the value.
        Assert.IsGreaterThanOrEqualTo(
            0.08 * Surface.Width - 0.5,
            (World.Left - visible.Left) * viewport.Scale,
            "horizontal slack is at least the padding");
        Assert.IsGreaterThanOrEqualTo(
            0.08 * Surface.Height - 0.5,
            (World.Top - visible.Top) * viewport.Scale,
            "vertical slack is at least the padding");
    }

    [TestMethod]
    public void FitToShowsTheWholeGraph()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.FitTo(World);

        var visible = viewport.GetVisibleWorldRect();
        Assert.IsLessThanOrEqualTo(World.Left, visible.Left, "left edge");
        Assert.IsGreaterThanOrEqualTo(World.Right, visible.Right, "right edge");
        Assert.IsLessThanOrEqualTo(World.Top, visible.Top, "top edge");
        Assert.IsGreaterThanOrEqualTo(World.Bottom, visible.Bottom, "bottom edge");
    }

    // ---------------------------------------------------------------------- zoom

    [TestMethod]
    public void ZoomAtKeepsTheAnchorPointInPlace()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);

        var anchor = new SKPoint(120f, 90f);
        var before = viewport.ScreenToWorld(anchor);

        viewport.ZoomAt(anchor, 1.25f);

        var after = viewport.ScreenToWorld(anchor);
        AssertClose(before.X, after.X, "anchor X should not move");
        AssertClose(before.Y, after.Y, "anchor Y should not move");
    }

    [TestMethod]
    public void ZoomAtClampsToTheSupportedRange()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.ZoomAt(viewport.SurfaceCenter, 1000f);
        Assert.AreEqual(GraphViewport.MaxScale, viewport.Scale, 0.0001, "clamped to MaxScale");

        viewport.ZoomAt(viewport.SurfaceCenter, 0.0001f);
        Assert.AreEqual(GraphViewport.MinScale, viewport.Scale, 0.0001, "clamped to MinScale");
    }

    [TestMethod]
    [DataRow(float.NaN)]
    [DataRow(0f)]
    [DataRow(-2f)]
    [DataRow(float.PositiveInfinity)]
    public void ZoomAtIgnoresUnusableFactors(float factor)
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);
        var scale = viewport.Scale;

        viewport.ZoomAt(viewport.SurfaceCenter, factor);

        Assert.AreEqual(scale, viewport.Scale, "an unusable factor must leave the zoom alone");
    }

    [TestMethod]
    public void ZoomAroundWorldKeepsAnOnScreenAnchor()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);

        var screenPoint = viewport.SurfaceCenter;
        var before = viewport.ScreenToWorld(screenPoint);

        viewport.ZoomAroundWorld(before, 1.5f);

        var after = viewport.ScreenToWorld(screenPoint);
        AssertClose(before.X, after.X, "anchor X should not move");
        AssertClose(before.Y, after.Y, "anchor Y should not move");
    }

    [TestMethod]
    public void ZoomAroundWorldDoesNotDragAFarAnchorIntoView()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);
        var before = viewport.Scale;

        // Far outside the current view, in the direction the clamp pulls back from.
        var far = new SKPoint(World.Left - 5000f, World.Top - 5000f);

        viewport.ZoomAroundWorld(far, 1.2f);

        var farOnScreen = viewport.WorldToScreen(far);
        Assert.IsLessThan(0f, farOnScreen.X, "the anchor X should stay off screen");
        Assert.IsLessThan(0f, farOnScreen.Y, "the anchor Y should stay off screen");
        Assert.IsGreaterThan(before * 1.1f, viewport.Scale, "it should still have zoomed");
    }

    // ----------------------------------------------------------------------- pan

    [TestMethod]
    public void PanByScreenDeltaMovesTheContentByThatDelta()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);
        var before = viewport.ScreenToWorld(SKPoint.Empty);

        viewport.PanByScreenDelta(25f, -40f);

        var after = viewport.ScreenToWorld(SKPoint.Empty);
        AssertClose(25f / viewport.Scale, before.X - after.X, "content follows the pointer on X");
        AssertClose(-40f / viewport.Scale, before.Y - after.Y, "content follows the pointer on Y");
    }

    [TestMethod]
    public void PanByScreenDeltaIgnoresAZeroDelta()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);
        var translation = viewport.Translation;

        viewport.PanByScreenDelta(0f, 0f);

        Assert.AreEqual(translation.X, viewport.Translation.X);
        Assert.AreEqual(translation.Y, viewport.Translation.Y);
    }

    // ------------------------------------------------------------ centre / reset

    [TestMethod]
    public void CenterOnPutsThePointAtTheCentre()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);

        viewport.CenterOn(SKPoint.Empty);

        var center = viewport.ScreenToWorld(viewport.SurfaceCenter);
        AssertClose(0, center.X, "centred X");
        AssertClose(0, center.Y, "centred Y");
    }

    [TestMethod]
    public void ResetRestoresScaleAndOrigin()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);

        viewport.Reset();

        Assert.AreEqual(1f, viewport.Scale, 0.0001);
        var center = viewport.ScreenToWorld(viewport.SurfaceCenter);
        AssertClose(0, center.X, "origin centred X");
        AssertClose(0, center.Y, "origin centred Y");
    }

    // -------------------------------------------------------------------- resize

    [TestMethod]
    public void ResizeSurfaceKeepsTheCentre()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);
        var before = viewport.ScreenToWorld(viewport.SurfaceCenter);
        var scale = viewport.Scale;

        viewport.ResizeSurface(new SKSize(1000f, 500f));

        var after = viewport.ScreenToWorld(viewport.SurfaceCenter);
        AssertClose(before.X, after.X, "centre X should survive", 0.05);
        AssertClose(before.Y, after.Y, "centre Y should survive", 0.05);
        Assert.AreEqual(scale, viewport.Scale, 0.0001, "a resize must not zoom");
        Assert.AreEqual(1000f, viewport.SurfaceSize.Width, "the new size is recorded");
    }

    [TestMethod]
    public void ResizeSurfaceAttachesWhenThereIsNoSurfaceYet()
    {
        var viewport = new GraphViewport();

        viewport.ResizeSurface(Surface);

        Assert.IsTrue(viewport.HasSurface);
        Assert.AreEqual(1f, viewport.Scale, 0.0001, "nothing to preserve, so nothing changes");
    }

    [TestMethod]
    public void ResizeSurfaceIsStableAcrossARoundTrip()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);
        var before = viewport.ScreenToWorld(viewport.SurfaceCenter);

        viewport.ResizeSurface(new SKSize(640f, 480f));
        viewport.ResizeSurface(Surface);

        var after = viewport.ScreenToWorld(viewport.SurfaceCenter);
        AssertClose(before.X, after.X, "centre X survives resize and back", 0.05);
        AssertClose(before.Y, after.Y, "centre Y survives resize and back", 0.05);
    }

    // ------------------------------------------------------------ coordinate map

    [TestMethod]
    public void ScreenAndWorldCoordinatesRoundTrip()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);

        var probe = new SKPoint(123f, 456f);

        var roundTripped = viewport.ScreenToWorld(viewport.WorldToScreen(probe));

        AssertClose(probe.X, roundTripped.X, "round trip X");
        AssertClose(probe.Y, roundTripped.Y, "round trip Y");
    }

    [TestMethod]
    public void GetVisibleWorldRectCoversTheSurface()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);

        var visible = viewport.GetVisibleWorldRect();
        var topLeft = viewport.ScreenToWorld(SKPoint.Empty);
        var bottomRight = viewport.ScreenToWorld(new SKPoint(Surface.Width, Surface.Height));

        AssertClose(topLeft.X, visible.Left, "left edge maps to the surface origin");
        AssertClose(topLeft.Y, visible.Top, "top edge maps to the surface origin");
        AssertClose(bottomRight.X, visible.Right, "right edge maps to the far corner");
        AssertClose(bottomRight.Y, visible.Bottom, "bottom edge maps to the far corner");
    }

    // ---------------------------------------------------------- degenerate input

    [TestMethod]
    public void AZeroSizedSurfaceCountsAsDetached()
    {
        var viewport = new GraphViewport();

        viewport.AttachSurface(new SKSize(0f, 0f));

        Assert.IsFalse(viewport.HasSurface);
    }

    [TestMethod]
    public void FitToWithAZeroSizedSurfaceIsANoOp()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(new SKSize(0f, 0f));

        viewport.FitTo(World);

        Assert.AreEqual(1f, viewport.Scale, 0.0001);
    }

    [TestMethod]
    public void FitToWithEmptyBoundsDoesNotProduceNaN()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.FitTo(SKRect.Empty);

        Assert.IsFalse(float.IsNaN(viewport.Scale));
        Assert.IsGreaterThan(0f, viewport.Scale);
    }

    // -------------------------------------------------------------- pan clamping

    [TestMethod]
    public void PanningIsUnrestrictedWithoutBounds()
    {
        var viewport = new GraphViewport();
        viewport.AttachSurface(Surface);

        viewport.PanByScreenDelta(1e6f, 1e6f);

        Assert.IsGreaterThan(1e5f, viewport.Translation.X);
    }

    [TestMethod]
    public void PanningOverflowingContentStopsAtTheSlack()
    {
        var viewport = ZoomedInto(BigSquare, 2f);
        Assert.IsGreaterThan(Surface.Width, BigSquare.Width * viewport.Scale, "the test needs overflowing content");
        Assert.IsGreaterThan(Surface.Height, BigSquare.Height * viewport.Scale, "on both axes");

        viewport.PanByScreenDelta(1e6f, 1e6f);

        AssertClose(Surface.Width - Slack(Surface.Width), viewport.Translation.X, "stopped at the right slack");
        AssertClose(Surface.Height - Slack(Surface.Height), viewport.Translation.Y, "stopped at the bottom slack");
        AssertClose(Slack(Surface.Width), OverlapOnScreen(viewport, BigSquare), "the slack stays visible");
    }

    [TestMethod]
    public void PanningTheOtherWayStopsAtTheSlack()
    {
        var viewport = ZoomedInto(BigSquare, 2f);
        var contentWidth = BigSquare.Width * viewport.Scale;
        var contentHeight = BigSquare.Height * viewport.Scale;

        viewport.PanByScreenDelta(-1e6f, -1e6f);

        AssertClose(Slack(Surface.Width) - contentWidth, viewport.Translation.X, "stopped at the left slack");
        AssertClose(Slack(Surface.Height) - contentHeight, viewport.Translation.Y, "stopped at the top slack");
        AssertClose(Slack(Surface.Width), OverlapOnScreen(viewport, BigSquare), "the slack stays visible");
    }

    [TestMethod]
    public void TheClampIsIdempotent()
    {
        var viewport = ZoomedInto(BigSquare, 2f);
        viewport.PanByScreenDelta(-1e6f, -1e6f);
        var settled = viewport.Translation;

        viewport.PanByScreenDelta(-1e6f, -1e6f);

        Assert.AreEqual(settled.X, viewport.Translation.X, 0.0001);
        Assert.AreEqual(settled.Y, viewport.Translation.Y, 0.0001);
    }

    [TestMethod]
    public void SmallPansInsideTheAllowedRangeApplyInFull()
    {
        var viewport = ZoomedInto(BigSquare, 2f);
        var before = viewport.Translation;

        viewport.PanByScreenDelta(12f, -8f);

        AssertClose(before.X + 12f, viewport.Translation.X, "X applied in full");
        AssertClose(before.Y - 8f, viewport.Translation.Y, "Y applied in full");
    }

    [TestMethod]
    public void OnlyTheOverflowingAxisIsClamped()
    {
        var viewport = ZoomedInto(Wide, 1.5f);

        viewport.PanByScreenDelta(1e6f, 1e6f);

        AssertClose(Surface.Width - Slack(Surface.Width), viewport.Translation.X, "X is clamped");
        Assert.IsGreaterThan(1e5f, viewport.Translation.Y, "Y is free");
    }

    [TestMethod]
    public void ZeroSlackPinsTheContentToTheSurfaceEdge()
    {
        var viewport = ZoomedInto(BigSquare, 2f, slackRatio: 0f);

        viewport.PanByScreenDelta(1e6f, 1e6f);

        AssertClose(Surface.Width, viewport.Translation.X, "pinned on the right");
        AssertClose(Surface.Height, viewport.Translation.Y, "pinned on the bottom");
    }

    [TestMethod]
    public void ContentSmallerThanTheSurfacePansFreely()
    {
        var viewport = new GraphViewport { Bounds = new SKRect(0, 0, 100, 100) };
        viewport.AttachSurface(Surface);

        viewport.PanByScreenDelta(1e6f, 1e6f);

        Assert.IsGreaterThan(1e5f, viewport.Translation.X);
    }

    [TestMethod]
    public void AFittedViewSitsInsideTheClampRange()
    {
        var viewport = new GraphViewport { Bounds = BigSquare };
        viewport.AttachSurface(Surface);

        viewport.FitTo(BigSquare);

        AssertInClampRange("X", viewport.Translation.X, Surface.Width, BigSquare.Width, viewport.Scale);
        AssertInClampRange("Y", viewport.Translation.Y, Surface.Height, BigSquare.Height, viewport.Scale);
    }

    [TestMethod]
    public void TheDemoGraphsFitSitsInsideTheClampRange()
    {
        // The demo bounds start left of and above the origin, so this is the case where
        // clamping from the size instead of the edges would put the fitted view outside
        // the range. BigSquare, used above, starts at the origin and cannot catch that.
        var viewport = new GraphViewport { Bounds = World };
        viewport.AttachSurface(Surface);

        viewport.FitTo(World);

        AssertInClampRange("X", viewport.Translation.X, Surface.Width, World.Width, viewport.Scale);
        AssertInClampRange("Y", viewport.Translation.Y, Surface.Height, World.Height, viewport.Scale);
    }

    [TestMethod]
    public void CentringOnTheGraphEdgeIsNotClamped()
    {
        var viewport = ZoomedInto(BigSquare, 2f);

        viewport.CenterOn(new SKPoint(BigSquare.Left, BigSquare.Top));

        var center = viewport.ScreenToWorld(viewport.SurfaceCenter);
        AssertClose(BigSquare.Left, center.X, "the minimap can still centre on the edge");
    }

    // ------------------------- clamping with bounds that have an origin offset

    [TestMethod]
    public void ClampingHandlesBoundsWithANegativeOrigin()
    {
        // The demo graph is inset by a margin, so its bounds start left of and above the
        // world origin. Clamping from the bounds' size instead of its edges would be off by
        // that margin, and the graph could still be dragged out of sight.
        var viewport = ZoomedInto(World, 2f);
        var scale = viewport.Scale;

        viewport.PanByScreenDelta(1e6f, 1e6f);

        AssertClose(Surface.Width - Slack(Surface.Width) - (World.Left * scale), viewport.Translation.X, "high edge X");
        AssertClose(Surface.Height - Slack(Surface.Height) - (World.Top * scale), viewport.Translation.Y, "high edge Y");
        AssertClose(Slack(Surface.Width), OverlapOnScreen(viewport, World), "the slack stays visible at the high edge");

        viewport.PanByScreenDelta(-1e6f, -1e6f);

        AssertClose(Slack(Surface.Width) - (World.Right * scale), viewport.Translation.X, "low edge X");
        AssertClose(Slack(Surface.Width), OverlapOnScreen(viewport, World), "the slack stays visible at the low edge");
    }

    [TestMethod]
    [DataRow(1.5f)]
    [DataRow(2f)]
    [DataRow(4f)]
    [DataRow(8f)]
    public void TheGraphAlwaysKeepsTheSlackOnScreenOnceItOverflows(float zoom)
    {
        var viewport = new GraphViewport { Bounds = World };
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);
        viewport.ZoomAt(viewport.SurfaceCenter, zoom);

        Assert.IsGreaterThan(Surface.Width, World.Width * viewport.Scale, "zoom x" + zoom + " should overflow X");
        Assert.IsGreaterThan(Surface.Height, World.Height * viewport.Scale, "zoom x" + zoom + " should overflow Y");

        viewport.PanByScreenDelta(4000f, 4000f);
        Assert.IsGreaterThanOrEqualTo(
            Slack(Surface.Width) - 0.01,
            OverlapOnScreen(viewport, World),
            "after panning one way the graph is still partly visible");

        viewport.PanByScreenDelta(-4000f, -4000f);
        Assert.IsGreaterThanOrEqualTo(
            Slack(Surface.Width) - 0.01,
            OverlapOnScreen(viewport, World),
            "after panning the other way the graph is still partly visible");
    }

    [TestMethod]
    public void AGraphThatFitsTheSurfaceCanBePannedAway()
    {
        // Deliberate: at that zoom the whole graph is on screen, so there is nothing to lose
        // and panning it away is expected behaviour.
        var viewport = new GraphViewport { Bounds = World };
        viewport.AttachSurface(Surface);
        viewport.FitTo(World);
        viewport.ZoomAt(viewport.SurfaceCenter, 0.4f);

        Assert.IsLessThanOrEqualTo(Surface.Width, World.Width * viewport.Scale, "the graph now fits");

        viewport.PanByScreenDelta(1e6f, 1e6f);

        Assert.IsGreaterThan(1e5f, viewport.Translation.X);
    }

    private static void AssertInClampRange(string axis, float translation, float surfaceExtent, float contentExtent, float scale)
    {
        var slack = Slack(surfaceExtent);
        var minimum = slack - (contentExtent * scale);
        var maximum = surfaceExtent - slack;

        Assert.IsGreaterThanOrEqualTo(minimum - 0.01, translation, $"{axis} should not be below the clamp range");
        Assert.IsLessThanOrEqualTo(maximum + 0.01, translation, $"{axis} should not be above the clamp range");
    }
}
