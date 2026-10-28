namespace Strongthium.FolderSize;

/// <summary>A rectangle, in whatever units it's drawn in.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width * Height;
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Smaller by the given margins, never below nothing.</summary>
    public Box Inset(double left, double top, double right, double bottom) =>
        new(X + left, Y + top, Math.Max(0, Width - left - right), Math.Max(0, Height - top - bottom));
}

/// <summary>
/// Lays out a treemap: each thing a rectangle with an area in proportion to its size, in rows kept as near square as
/// they can be (Bruls, Huizing and van Wijk's squarified treemaps, 2000), so that sizes can be compared by eye.
/// </summary>
public static class Treemap
{
    /// <summary>A rectangle for each weight, in order; weights of 0 or less get an empty one. Weights given largest
    /// first lay out best.</summary>
    public static Box[] Squarify(IReadOnlyList<double> weights, Box bounds)
    {
        var boxes = new Box[weights.Count];
        var items = Enumerable.Range(0, weights.Count).Where(i => weights[i] > 0).ToArray();
        var total = items.Sum(i => weights[i]);
        if (total <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return boxes;
        var scale = bounds.Area / total;
        var rest = bounds;

        var start = 0;
        while (start < items.Length)
        {
            // The row takes items while that makes its worst rectangle less long and thin.
            var side = Math.Min(rest.Width, rest.Height);
            double sum = 0, largest = 0, smallest = double.MaxValue, worst = double.MaxValue;
            var end = start;
            while (end < items.Length)
            {
                var area = weights[items[end]] * scale;
                double nextSum = sum + area, nextLargest = Math.Max(largest, area), nextSmallest = Math.Min(smallest, area);
                var nextWorst = Worst(nextSum, nextLargest, nextSmallest, side);
                if (end > start && nextWorst > worst) break;
                (sum, largest, smallest, worst) = (nextSum, nextLargest, nextSmallest, nextWorst);
                end++;
            }
            // The last row takes what's left, so that rounding leaves no gap.
            if (end == items.Length) sum = rest.Area;
            rest = LayRow(items.AsSpan(start, end - start), weights, scale, sum, rest, boxes, last: end == items.Length);
            start = end;
        }
        return boxes;
    }

    private static double Worst(double sum, double largest, double smallest, double side)
    {
        if (sum <= 0 || smallest <= 0 || side <= 0) return double.MaxValue;
        double sideSquared = side * side, sumSquared = sum * sum;
        return Math.Max(sideSquared * largest / sumSquared, sumSquared / (sideSquared * smallest));
    }

    /// <summary>Lays a row along the short side of what's left, and returns what's left after it.</summary>
    private static Box LayRow(ReadOnlySpan<int> row, IReadOnlyList<double> weights, double scale, double sum, Box rest, Box[] boxes, bool last)
    {
        var rowArea = 0.0;
        foreach (var i in row) rowArea += weights[i] * scale;
        // On the last row, each takes its share of exactly what's left.
        var stretch = last && rowArea > 0 ? sum / rowArea : 1;
        if (rest.Width >= rest.Height)
        {
            // A column down the left.
            var thickness = rest.Height > 0 ? Math.Min(rest.Width, sum / rest.Height) : 0;
            var y = rest.Y;
            for (var k = 0; k < row.Length; k++)
            {
                var height = k == row.Length - 1 ? rest.Bottom - y : thickness > 0 ? weights[row[k]] * scale * stretch / thickness : 0;
                boxes[row[k]] = new Box(rest.X, y, thickness, Math.Max(0, height));
                y += height;
            }
            return new Box(rest.X + thickness, rest.Y, Math.Max(0, rest.Width - thickness), rest.Height);
        }
        else
        {
            // A row across the top.
            var thickness = rest.Width > 0 ? Math.Min(rest.Height, sum / rest.Width) : 0;
            var x = rest.X;
            for (var k = 0; k < row.Length; k++)
            {
                var width = k == row.Length - 1 ? rest.Right - x : thickness > 0 ? weights[row[k]] * scale * stretch / thickness : 0;
                boxes[row[k]] = new Box(x, rest.Y, Math.Max(0, width), thickness);
                x += width;
            }
            return new Box(rest.X, rest.Y + thickness, rest.Width, Math.Max(0, rest.Height - thickness));
        }
    }
}
