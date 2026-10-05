using SkiaSharp;

namespace BlazorSkiaSharp.Graphs;

/// <summary>
/// Draws a <see cref="GraphModel"/> using world coordinates, so the caller only has to
/// apply a <see cref="GraphViewport"/> transform before calling <see cref="Draw"/>.
/// </summary>
/// <remarks>
/// The Skia objects are allocated once and reused between frames. Only the parts of the
/// graph that intersect the visible rectangle are drawn, and labels disappear once the
/// nodes get too small to read them, which keeps zooming out cheap.
/// </remarks>
public sealed class GraphRenderer : IDisposable
{
    private static readonly SKColor Background = new(0xF8, 0xF9, 0xFB);
    private static readonly SKColor GridColor = new(0xE1, 0xE6, 0xEE);
    private static readonly SKColor AxisColor = new(0xC3, 0xCD, 0xDC);
    private static readonly SKColor EdgeColor = new(0x9A, 0xA8, 0xC0);
    private static readonly SKColor LabelColor = SKColors.White;

    /// <summary>Below this on-screen radius a node is too small to deserve its label.</summary>
    private const float MinimumLabelRadius = 17f;

    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _ring = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _edge = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _label = new() { IsAntialias = true, Color = LabelColor };
    private readonly SKFont _labelFont = new(SKTypeface.Default, 30f);
    private bool _disposed;

    /// <summary>Background colour of the surface the graph is drawn on.</summary>
    public static SKColor SurfaceColor => Background;

    /// <summary>Edge colour, reused by the minimap for its simplified drawing.</summary>
    public static SKColor EdgeStrokeColor => EdgeColor;

    /// <summary>Draws the graph over the whole <paramref name="visibleWorld"/> area.</summary>
    public void Draw(SKCanvas canvas, GraphModel graph, SKRect visibleWorld, float scale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        DrawGrid(canvas, visibleWorld, scale);
        DrawEdges(canvas, graph, visibleWorld, scale);
        DrawNodes(canvas, graph, visibleWorld, scale);
        DrawBounds(canvas, graph.Bounds, scale);
    }

    private void DrawGrid(SKCanvas canvas, SKRect visible, float scale)
    {
        if (scale < 0.04f)
            return;

        var step = NiceStep(56f / scale);
        if ((step * scale) < 8f)
            return;

        // Draw in world units, but never thinner than a hairline on screen.
        var width = MathF.Max(1f / scale, 0.4f);

        using var builder = new SKPathBuilder();

        var left = MathF.Floor(visible.Left / step) * step;
        var top = MathF.Floor(visible.Top / step) * step;

        for (var x = left; x <= visible.Right; x += step)
        {
            builder.MoveTo(x, visible.Top);
            builder.LineTo(x, visible.Bottom);
        }

        for (var y = top; y <= visible.Bottom; y += step)
        {
            builder.MoveTo(visible.Left, y);
            builder.LineTo(visible.Right, y);
        }

        using var path = builder.Snapshot();

        _ring.Color = GridColor;
        _ring.StrokeWidth = width;
        canvas.DrawPath(path, _ring);

        // World origin gets a slightly stronger line.
        _ring.Color = AxisColor;
        _ring.StrokeWidth = width * 2f;
        canvas.DrawLine(0, visible.Top, 0, visible.Bottom, _ring);
        canvas.DrawLine(visible.Left, 0, visible.Right, 0, _ring);
    }

    private void DrawEdges(SKCanvas canvas, GraphModel graph, SKRect visible, float scale)
    {
        _edge.Color = EdgeColor;
        _edge.StrokeWidth = MathF.Max(6f, 1.4f / scale);

        using var builder = new SKPathBuilder();
        var any = false;

        foreach (var edge in graph.Edges)
        {
            // Defensive: a malformed model should not break the whole render loop.
            if (edge.From < 0 || edge.From >= graph.Nodes.Count ||
                edge.To < 0 || edge.To >= graph.Nodes.Count)
                continue;

            var from = graph.Nodes[edge.From].Position;
            var to = graph.Nodes[edge.To].Position;

            if (!LineIntersectsVisibleRect(from, to, visible))
                continue;

            builder.MoveTo(from.X, from.Y);
            builder.LineTo(to.X, to.Y);
            any = true;
        }

        if (!any)
            return;

        using var path = builder.Snapshot();
        canvas.DrawPath(path, _edge);
    }

    private void DrawNodes(SKCanvas canvas, GraphModel graph, SKRect visible, float scale)
    {
        var metrics = _labelFont.Metrics;
        var textOffsetY = (metrics.Descent - metrics.Ascent) / 2f;
        var drawLabels = scale >= 0.18f;

        foreach (var node in graph.Nodes)
        {
            if (!IntersectsVisibleRect(node, visible))
                continue;

            // Never let a node shrink below a couple of pixels, or it disappears entirely.
            var radius = MathF.Max(node.Radius, 2f / scale);

            _fill.Color = node.Fill;
            canvas.DrawCircle(node.Position.X, node.Position.Y, radius, _fill);

            _ring.Color = node.Stroke;
            _ring.StrokeWidth = MathF.Max(4f, 1f / scale);
            canvas.DrawCircle(node.Position.X, node.Position.Y, radius, _ring);

            if (drawLabels && (radius * scale) >= MinimumLabelRadius)
                canvas.DrawText(node.Label, node.Position.X, node.Position.Y + textOffsetY, SKTextAlign.Center, _labelFont, _label);
        }
    }

    /// <summary>Outlines the whole graph, so its extent stays visible when zoomed in.</summary>
    private void DrawBounds(SKCanvas canvas, SKRect bounds, float scale)
    {
        if (canvas.QuickReject(bounds))
            return;

        _ring.Color = new SKColor(0x4C, 0x8B, 0xF6, 0x55);
        _ring.StrokeWidth = MathF.Max(4f, 1.5f / scale);
        canvas.DrawRect(bounds, _ring);
    }

    /// <summary>Rounds a raw grid step up to the nearest 1 / 2 / 5 / 10 multiple.</summary>
    private static float NiceStep(float raw)
    {
        if (raw <= 0f || float.IsNaN(raw) || float.IsInfinity(raw))
            return 1f;

        var exponent = MathF.Floor(MathF.Log10(raw));
        var magnitude = MathF.Pow(10f, exponent);
        var fraction = raw / magnitude;

        var nice = fraction switch
        {
            < 1.5f => 1f,
            < 3.5f => 2f,
            < 7.5f => 5f,
            _ => 10f,
        };

        return nice * magnitude;
    }

    private static bool IntersectsVisibleRect(GraphNode node, SKRect visible)
    {
        var radius = node.Radius + 8f;
        return visible.Left - radius <= node.Position.X &&
               visible.Right + radius >= node.Position.X &&
               visible.Top - radius <= node.Position.Y &&
               visible.Bottom + radius >= node.Position.Y;
    }

    private static bool LineIntersectsVisibleRect(SKPoint from, SKPoint to, SKRect visible)
        => (visible.Left <= from.X && from.X <= visible.Right && visible.Top <= from.Y && from.Y <= visible.Bottom) ||
           (visible.Left <= to.X && to.X <= visible.Right && visible.Top <= to.Y && to.Y <= visible.Bottom) ||
           LineIntersectsRect(from, to, visible);

    /// <summary>Liang-Barsky style segment/rectangle overlap test.</summary>
    private static bool LineIntersectsRect(SKPoint from, SKPoint to, SKRect rect)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        if (MathF.Abs(dx) < float.Epsilon)
        {
            return from.X >= rect.Left && from.X <= rect.Right &&
                   MathF.Max(from.Y, to.Y) >= rect.Top && MathF.Min(from.Y, to.Y) <= rect.Bottom;
        }

        if (MathF.Abs(dy) < float.Epsilon)
        {
            return from.Y >= rect.Top && from.Y <= rect.Bottom &&
                   MathF.Max(from.X, to.X) >= rect.Left && MathF.Min(from.X, to.X) <= rect.Right;
        }

        var t0 = 0f;
        var t1 = 1f;

        return Clip(-dx, from.X - rect.Left, ref t0, ref t1) &&
               Clip(dx, rect.Right - from.X, ref t0, ref t1) &&
               Clip(-dy, from.Y - rect.Top, ref t0, ref t1) &&
               Clip(dy, rect.Bottom - from.Y, ref t0, ref t1);
    }

    private static bool Clip(float denominator, float numerator, ref float t0, ref float t1)
    {
        if (MathF.Abs(denominator) < float.Epsilon)
            return numerator >= 0f;

        var t = numerator / denominator;

        if (denominator < 0f)
        {
            if (t > t1)
                return false;

            if (t > t0)
                t0 = t;
        }
        else
        {
            if (t < t0)
                return false;

            if (t < t1)
                t1 = t;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _fill.Dispose();
        _ring.Dispose();
        _edge.Dispose();
        _label.Dispose();
        _labelFont.Dispose();
    }
}
