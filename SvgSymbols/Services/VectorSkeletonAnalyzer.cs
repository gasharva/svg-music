using System.Globalization;
using System.Numerics;
using System.Text;

namespace SvgSymbols.Services;

public sealed record SkeletonPoint(float X, float Y, float Thickness, bool HorizontalScan);
public sealed record SkeletonSegment(SkeletonPoint A, SkeletonPoint B);
public sealed record SkeletonPolyline(
    bool HorizontalScan,
    IReadOnlyList<SkeletonPoint> RawPoints,
    IReadOnlyList<Vector2> SimplifiedPoints);

public sealed record VectorSkeletonAnalysis(
    IReadOnlyList<SkeletonPoint> Points,
    IReadOnlyList<SkeletonSegment> Segments,
    IReadOnlyList<SkeletonPolyline> Polylines);

/// <summary>
/// Experimental vector-only skeleton approximation.
/// It samples horizontal and vertical scanlines through filled contours, keeps interval midpoints,
/// connects nearby midpoint samples on adjacent scanlines, then traces those segments into chains
/// and simplifies each chain with Ramer-Douglas-Peucker.
/// This is intentionally diagnostic-first; it is not an exact medial-axis implementation.
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
            return new VectorSkeletonAnalysis(
                Array.Empty<SkeletonPoint>(),
                Array.Empty<SkeletonSegment>(),
                Array.Empty<SkeletonPolyline>());

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
            horizontalRows.Add(MidpointsFromPairs(xs, y, horizontal: true));
        }

        var verticalColumns = new List<List<SkeletonPoint>>();
        for (var i = 0; i < _verticalScanCount; i++)
        {
            var x = minX + (i + 0.5f) / _verticalScanCount * width;
            var ys = IntersectionsWithVertical(contours, x);
            verticalColumns.Add(MidpointsFromPairs(ys, x, horizontal: false));
        }

        var points = horizontalRows.SelectMany(x => x)
            .Concat(verticalColumns.SelectMany(x => x))
            .ToArray();

        var segments = new List<SkeletonSegment>();
        ConnectAdjacent(horizontalRows, segments, height / _horizontalScanCount, horizontalScan: true);
        ConnectAdjacent(verticalColumns, segments, width / _verticalScanCount, horizontalScan: false);

        var diagonal = MathF.Sqrt(width * width + height * height);
        var simplifyTolerance = Math.Max(diagonal * 0.012f, 1e-5f);
        var minimumChainLength = Math.Max(diagonal * 0.045f, 1e-5f);
        var polylines = BuildPolylines(segments, simplifyTolerance, minimumChainLength);

        return new VectorSkeletonAnalysis(points, segments, polylines);
    }

    /// <summary>
    /// Writes a second-stage diagnostic: raw midpoint chains are pale, RDP-simplified polylines
    /// are strong. It deliberately does not smooth curves yet, so bad topology remains visible.
    /// </summary>
    public void WriteLinesDiagnosticSvg(
        string path,
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        VectorSkeletonAnalysis analysis)
    {
        var all = contours.SelectMany(x => x).ToArray();
        if (all.Length == 0)
            return;

        var minX = all.Min(p => p.X);
        var maxX = all.Max(p => p.X);
        var minY = all.Min(p => p.Y);
        var maxY = all.Max(p => p.Y);
        var width = Math.Max(maxX - minX, 1e-6f);
        var height = Math.Max(maxY - minY, 1e-6f);
        var pad = Math.Max(1f, Math.Max(width, height) * 0.06f);
        var thinStroke = Math.Max(0.10f, Math.Min(width, height) * 0.004f);
        var strongStroke = Math.Max(0.22f, Math.Min(width, height) * 0.011f);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(minX - pad)} {F(minY - pad)} {F(width + 2 * pad)} {F(height + 2 * pad)}\">");

        sb.Append("<path fill=\"#777\" fill-opacity=\"0.10\" fill-rule=\"evenodd\" d=\"");
        foreach (var contour in contours.Where(x => x.Count >= 2))
        {
            sb.Append($"M {F(contour[0].X)} {F(contour[0].Y)} ");
            foreach (var p in contour.Skip(1))
                sb.Append($"L {F(p.X)} {F(p.Y)} ");
            sb.Append("Z ");
        }
        sb.AppendLine("\"/>");

        foreach (var line in analysis.Polylines)
        {
            if (line.RawPoints.Count >= 2)
            {
                var raw = string.Join(' ', line.RawPoints.Select(p => $"{F(p.X)},{F(p.Y)}"));
                sb.AppendLine($"<polyline points=\"{raw}\" fill=\"none\" stroke=\"#999\" stroke-width=\"{F(thinStroke)}\" opacity=\"0.45\"/>");
            }
        }

        foreach (var line in analysis.Polylines)
        {
            if (line.SimplifiedPoints.Count < 2)
                continue;

            var color = line.HorizontalScan ? "#d62728" : "#1f77b4";
            var simplified = string.Join(' ', line.SimplifiedPoints.Select(p => $"{F(p.X)},{F(p.Y)}"));
            sb.AppendLine($"<polyline points=\"{simplified}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{F(strongStroke)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\" opacity=\"0.92\"/>");

            foreach (var p in line.SimplifiedPoints)
                sb.AppendLine($"<circle cx=\"{F(p.X)}\" cy=\"{F(p.Y)}\" r=\"{F(strongStroke * 0.9f)}\" fill=\"{color}\"/>");
        }

        sb.AppendLine("</svg>");
        File.WriteAllText(path, sb.ToString());
    }

    private static IReadOnlyList<SkeletonPolyline> BuildPolylines(
        IReadOnlyList<SkeletonSegment> segments,
        float simplifyTolerance,
        float minimumChainLength)
    {
        var result = new List<SkeletonPolyline>();
        foreach (var horizontal in new[] { true, false })
        {
            var oriented = segments
                .Where(x => x.A.HorizontalScan == horizontal && x.B.HorizontalScan == horizontal)
                .ToArray();
            if (oriented.Length == 0)
                continue;

            var adjacency = new Dictionary<SkeletonPoint, List<SkeletonPoint>>();
            foreach (var segment in oriented)
            {
                AddNeighbor(adjacency, segment.A, segment.B);
                AddNeighbor(adjacency, segment.B, segment.A);
            }

            var usedEdges = new HashSet<EdgeKey>();
            var starts = adjacency
                .Where(x => x.Value.Count != 2)
                .Select(x => x.Key)
                .ToArray();

            foreach (var start in starts)
            foreach (var next in adjacency[start])
            {
                var key = EdgeKey.Create(start, next);
                if (usedEdges.Contains(key))
                    continue;
                AddChain(Trace(start, next, adjacency, usedEdges), horizontal);
            }

            // Closed loops have no degree != 2 endpoint, so consume whatever edges remain.
            foreach (var segment in oriented)
            {
                var key = EdgeKey.Create(segment.A, segment.B);
                if (usedEdges.Contains(key))
                    continue;
                AddChain(Trace(segment.A, segment.B, adjacency, usedEdges), horizontal);
            }
        }

        return result;

        void AddChain(IReadOnlyList<SkeletonPoint> chain, bool horizontal)
        {
            if (chain.Count < 4 || Length(chain) < minimumChainLength)
                return;

            var raw = chain.ToArray();
            var vectors = raw.Select(p => new Vector2(p.X, p.Y)).ToArray();
            var simplified = Rdp(vectors, simplifyTolerance);
            if (simplified.Count >= 2)
                result.Add(new SkeletonPolyline(horizontal, raw, simplified));
        }
    }

    private static IReadOnlyList<SkeletonPoint> Trace(
        SkeletonPoint start,
        SkeletonPoint next,
        IReadOnlyDictionary<SkeletonPoint, List<SkeletonPoint>> adjacency,
        ISet<EdgeKey> usedEdges)
    {
        var chain = new List<SkeletonPoint> { start };
        var previous = start;
        var current = next;
        usedEdges.Add(EdgeKey.Create(previous, current));
        chain.Add(current);

        while (adjacency.TryGetValue(current, out var neighbors))
        {
            var candidates = neighbors
                .Where(x => !EqualityComparer<SkeletonPoint>.Default.Equals(x, previous))
                .Where(x => !usedEdges.Contains(EdgeKey.Create(current, x)))
                .ToArray();
            if (candidates.Length == 0)
                break;

            // Prefer the continuation that bends least. This suppresses many branch-hopping artifacts.
            var incoming = new Vector2(current.X - previous.X, current.Y - previous.Y);
            var chosen = candidates
                .OrderBy(x => TurnCost(incoming, new Vector2(x.X - current.X, x.Y - current.Y)))
                .ThenBy(x => Vector2.DistanceSquared(new Vector2(current.X, current.Y), new Vector2(x.X, x.Y)))
                .First();

            previous = current;
            current = chosen;
            usedEdges.Add(EdgeKey.Create(previous, current));
            chain.Add(current);

            if (EqualityComparer<SkeletonPoint>.Default.Equals(current, start))
                break;
            if (adjacency[current].Count != 2)
                break;
        }

        return chain;
    }

    private static float TurnCost(Vector2 a, Vector2 b)
    {
        if (a.LengthSquared() < 1e-12f || b.LengthSquared() < 1e-12f)
            return 1f;
        a = Vector2.Normalize(a);
        b = Vector2.Normalize(b);
        return 1f - Math.Clamp(Vector2.Dot(a, b), -1f, 1f);
    }

    private static void AddNeighbor(
        IDictionary<SkeletonPoint, List<SkeletonPoint>> adjacency,
        SkeletonPoint a,
        SkeletonPoint b)
    {
        if (!adjacency.TryGetValue(a, out var list))
        {
            list = new List<SkeletonPoint>();
            adjacency[a] = list;
        }
        if (!list.Contains(b))
            list.Add(b);
    }

    private static float Length(IReadOnlyList<SkeletonPoint> points)
    {
        float total = 0;
        for (var i = 1; i < points.Count; i++)
            total += Vector2.Distance(
                new Vector2(points[i - 1].X, points[i - 1].Y),
                new Vector2(points[i].X, points[i].Y));
        return total;
    }

    private static IReadOnlyList<Vector2> Rdp(IReadOnlyList<Vector2> points, float epsilon)
    {
        if (points.Count <= 2)
            return points.ToArray();

        var maxDistance = 0f;
        var index = -1;
        for (var i = 1; i < points.Count - 1; i++)
        {
            var distance = DistanceToSegment(points[i], points[0], points[^1]);
            if (distance <= maxDistance)
                continue;
            maxDistance = distance;
            index = i;
        }

        if (maxDistance <= epsilon || index < 0)
            return new[] { points[0], points[^1] };

        var left = Rdp(points.Take(index + 1).ToArray(), epsilon);
        var right = Rdp(points.Skip(index).ToArray(), epsilon);
        return left.Take(left.Count - 1).Concat(right).ToArray();
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var denominator = ab.LengthSquared();
        if (denominator < 1e-12f)
            return Vector2.Distance(p, a);
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / denominator, 0f, 1f);
        return Vector2.Distance(p, a + t * ab);
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

    private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private readonly record struct EdgeKey(SkeletonPoint A, SkeletonPoint B)
    {
        public static EdgeKey Create(SkeletonPoint a, SkeletonPoint b)
        {
            var ah = HashCode.Combine(a.X, a.Y, a.HorizontalScan);
            var bh = HashCode.Combine(b.X, b.Y, b.HorizontalScan);
            return ah <= bh ? new EdgeKey(a, b) : new EdgeKey(b, a);
        }
    }
}
