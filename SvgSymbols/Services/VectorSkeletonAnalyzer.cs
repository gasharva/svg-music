using System.Numerics;

namespace SvgSymbols.Services;

public sealed record SkeletonPoint(float X, float Y, float Thickness, bool HorizontalScan);
public sealed record SkeletonSegment(SkeletonPoint A, SkeletonPoint B);
public sealed record VectorSkeletonAnalysis(
    IReadOnlyList<SkeletonPoint> Points,
    IReadOnlyList<SkeletonSegment> Segments);

/// <summary>
/// Experimental vector-only skeleton approximation.
/// It samples horizontal and vertical scanlines through filled contours, keeps interval midpoints,
/// then connects nearby midpoint samples on adjacent scanlines. This is intentionally simple and
/// diagnostic-first; it is not an exact medial-axis implementation.
/// </summary>
public sealed class VectorSkeletonAnalyzer
{
    private readonly int _horizontalScanCount;
    private readonly int _verticalScanCount;

    public VectorSkeletonAnalyzer(int horizontalScanCount = 72, int verticalScanCount = 72)
    {
        _horizontalScanCount = Math.Max(16, horizontalScanCount);
        _verticalScanCount = Math.Max(16, verticalScanCount);
    }

    public VectorSkeletonAnalysis Analyze(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var all = contours.SelectMany(x => x).ToArray();
        if (all.Length == 0)
            return new VectorSkeletonAnalysis(Array.Empty<SkeletonPoint>(), Array.Empty<SkeletonSegment>());

        var minX = all.Min(p => p.X);
        var maxX = all.Max(p => p.X);
        var minY = all.Min(p => p.Y);
        var maxY = all.Max(p => p.Y);
        var width = Math.Max(maxX - minX, 1e-6f);
        var height = Math.Max(maxY - minY, 1e-6f);

        var horizontalRows = new List<List<SkeletonPoint>>();
        for (var i = 0; i < _horizontalScanCount; i++)
        {
            var y = minY + (i + 0.5f) / _horizontalScanCount * height;
            var xs = IntersectionsWithHorizontal(contours, y);
            var row = MidpointsFromPairs(xs, y, horizontal: true);
            horizontalRows.Add(row);
        }

        var verticalColumns = new List<List<SkeletonPoint>>();
        for (var i = 0; i < _verticalScanCount; i++)
        {
            var x = minX + (i + 0.5f) / _verticalScanCount * width;
            var ys = IntersectionsWithVertical(contours, x);
            var column = MidpointsFromPairs(ys, x, horizontal: false);
            verticalColumns.Add(column);
        }

        var points = horizontalRows.SelectMany(x => x)
            .Concat(verticalColumns.SelectMany(x => x))
            .ToArray();
        var segments = new List<SkeletonSegment>();
        ConnectAdjacent(horizontalRows, segments, primaryStep: height / _horizontalScanCount, horizontalScan: true);
        ConnectAdjacent(verticalColumns, segments, primaryStep: width / _verticalScanCount, horizontalScan: false);

        return new VectorSkeletonAnalysis(points, segments);
    }

    private static List<SkeletonPoint> MidpointsFromPairs(IReadOnlyList<float> crossings, float fixedCoord, bool horizontal)
    {
        var result = new List<SkeletonPoint>();
        for (var i = 0; i + 1 < crossings.Count; i += 2)
        {
            var a = crossings[i];
            var b = crossings[i + 1];
            if (b <= a)
                continue;

            var mid = (a + b) * 0.5f;
            var thickness = b - a;
            result.Add(horizontal
                ? new SkeletonPoint(mid, fixedCoord, thickness, true)
                : new SkeletonPoint(fixedCoord, mid, thickness, false));
        }
        return result;
    }

    private static void ConnectAdjacent(
        IReadOnlyList<List<SkeletonPoint>> rows,
        ICollection<SkeletonSegment> output,
        float primaryStep,
        bool horizontalScan)
    {
        for (var i = 0; i + 1 < rows.Count; i++)
        {
            var a = rows[i];
            var b = rows[i + 1];
            if (a.Count == 0 || b.Count == 0)
                continue;

            foreach (var point in a)
            {
                var nearest = b
                    .Select(candidate => new
                    {
                        Point = candidate,
                        Delta = horizontalScan
                            ? Math.Abs(candidate.X - point.X)
                            : Math.Abs(candidate.Y - point.Y),
                        ThicknessDelta = Math.Abs(candidate.Thickness - point.Thickness)
                    })
                    .OrderBy(x => x.Delta + 0.20f * x.ThicknessDelta)
                    .First();

                var allowedJump = Math.Max(primaryStep * 2.5f, Math.Max(point.Thickness, nearest.Point.Thickness) * 0.85f);
                if (nearest.Delta <= allowedJump)
                    output.Add(new SkeletonSegment(point, nearest.Point));
            }
        }
    }

    private static IReadOnlyList<float> IntersectionsWithHorizontal(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        float y)
    {
        var result = new List<float>();
        foreach (var contour in contours)
        {
            if (contour.Count < 2)
                continue;
            for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
            {
                var a = contour[j];
                var b = contour[i];
                if ((a.Y > y) == (b.Y > y))
                    continue;
                var t = (y - a.Y) / (b.Y - a.Y);
                result.Add(a.X + t * (b.X - a.X));
            }
        }
        result.Sort();
        return Deduplicate(result);
    }

    private static IReadOnlyList<float> IntersectionsWithVertical(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        float x)
    {
        var result = new List<float>();
        foreach (var contour in contours)
        {
            if (contour.Count < 2)
                continue;
            for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
            {
                var a = contour[j];
                var b = contour[i];
                if ((a.X > x) == (b.X > x))
                    continue;
                var t = (x - a.X) / (b.X - a.X);
                result.Add(a.Y + t * (b.Y - a.Y));
            }
        }
        result.Sort();
        return Deduplicate(result);
    }

    private static IReadOnlyList<float> Deduplicate(IReadOnlyList<float> sorted)
    {
        if (sorted.Count < 2)
            return sorted;
        var result = new List<float> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i] - result[^1]) > 1e-4f)
                result.Add(sorted[i]);
        }
        return result;
    }
}
