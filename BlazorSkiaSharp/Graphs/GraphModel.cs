using SkiaSharp;

namespace BlazorSkiaSharp.Graphs;

/// <summary>A node of the demo graph, positioned in world (graph) coordinates.</summary>
public sealed class GraphNode
{
    public required string Label { get; init; }

    /// <summary>Centre of the node in world coordinates.</summary>
    public required SKPoint Position { get; init; }

    public float Radius { get; init; } = 54f;

    public SKColor Fill { get; init; } = new(0x4C, 0x8B, 0xF6);

    public SKColor Stroke { get; init; } = new(0x1B, 0x3A, 0x6B);
}

/// <summary>A directed connection between two <see cref="GraphNode"/>s, by index.</summary>
public sealed class GraphEdge
{
    public required int From { get; init; }

    public required int To { get; init; }
}

/// <summary>
/// A set of nodes and edges living in a large world, big enough that it cannot
/// reasonably fit a browser window: that is what makes the pan / zoom meaningful.
/// </summary>
public sealed class GraphModel
{
    public required IReadOnlyList<GraphNode> Nodes { get; init; }

    public required IReadOnlyList<GraphEdge> Edges { get; init; }

    /// <summary>World rectangle covering the whole graph, including a margin.</summary>
    public required SKRect Bounds { get; init; }

    private static readonly SKColor[] Palette =
    [
        new(0x4C, 0x8B, 0xF6), // blue
        new(0x22, 0xC5, 0x5E), // green
        new(0xF6, 0x8B, 0x4C), // orange
        new(0xA8, 0x55, 0xF7), // violet
        new(0xF7, 0xC9, 0x48), // amber
        new(0x3A, 0xC4, 0xC4), // teal
    ];

    /// <summary>
    /// Builds a deterministic, layered graph so the demo always looks the same.
    /// </summary>
    public static GraphModel CreateDemo()
    {
        var random = new Random(1337);

        const int columnCount = 8;
        const float columnGap = 780f;
        const float rowGap = 420f;
        const float nodeRadius = 54f;
        const int edgesPerNode = 3;

        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        var columnCounts = new int[columnCount];

        // Overall height of the tallest column, so all columns share a middle band.
        var baseline = 0;
        for (var i = 0; i < 4; i++)
            baseline += random.Next(2, 6);

        for (var column = 0; column < columnCount; column++)
        {
            var centerRow = random.Next(baseline, baseline + 3);
            var amount = Math.Clamp(centerRow + random.Next(-2, 3), 3, 10);
            columnCounts[column] = amount;

            for (var row = 0; row < amount; row++)
            {
                nodes.Add(new GraphNode
                {
                    Label = $"N{column}-{row}",
                    Position = new SKPoint(column * columnGap, row * rowGap),
                    Radius = nodeRadius,
                    Fill = Palette[random.Next(Palette.Length)],
                    Stroke = new SKColor(0x1B, 0x3A, 0x6B),
                });
            }
        }

        var columnStart = 0;
        for (var column = 0; column < columnCount - 1; column++)
        {
            var nextStart = columnStart + columnCounts[column];

            for (var row = 0; row < columnCounts[column]; row++)
            {
                var from = columnStart + row;

                for (var i = 0; i < edgesPerNode; i++)
                {
                    edges.Add(new GraphEdge
                    {
                        From = from,
                        To = nextStart + random.Next(columnCounts[column + 1]),
                    });
                }
            }

            columnStart = nextStart;
        }

        var margin = nodeRadius + 220f;

        // NB: the constructor takes left / top / right / bottom, unlike SKRect.Create.
        var bounds = new SKRect(
            -margin,
            -margin,
            ((columnCount - 1) * columnGap) + margin,
            ((columnCounts.Max() - 1) * rowGap) + margin);

        return new GraphModel
        {
            Nodes = nodes,
            Edges = edges,
            Bounds = bounds,
        };
    }
}
