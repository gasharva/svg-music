using System.Globalization;
using System.Numerics;
using ShimSkiaSharp;
using Svg.Skia;

namespace SvgSymbols.Services;

public sealed record DigitHoleFeature(
    double CenterX,
    double CenterY,
    double AreaRatio);

public sealed record DigitStructuralFeatures(
    int RawContourCount,
    int ClosedContourCount,
    int OuterContourCount,
    int HoleCount,
    int MaxNestingDepth,
    double AspectRatio,
    double FillRatio,
    double NormalizedPerimeter,
    IReadOnlyList<DigitHoleFeature> Holes);

/// <summary>
/// Small diagnostic extractor for single time-signature digits.
/// It deliberately does no recognition. It only measures scale-independent vector topology
/// so different glyph families can be compared side by side.
/// </summary>
public sealed class DigitStructuralFeatureExtractor
{
    private const int CurveSteps = 20;

    public DigitStructuralFeatures Extract(string svgPath)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var picture = svg.Model
            ?? throw new InvalidOperationException($"Svg.Skia did not produce a retained scene model for '{svgPath}'.");

        var contours = new List<List<Vector2>>();
        ReadPicture(picture, SKMatrix.Identity, contours);

        var usable = contours
            .Where(x => x.Count >= 3)
            .Select(CloseIfNeeded)
            .Where(x => x.Count >= 4)
            .ToArray();

        if (usable.Length == 0)
            return new DigitStructuralFeatures(0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<DigitHoleFeature>());

        var bounds = Bounds(usable.SelectMany(x => x));
        var width = Math.Max(bounds.Right - bounds.Left, 1e-9);
        var height = Math.Max(bounds.Bottom - bounds.Top, 1e-9);
        var bboxArea = width * height;
        var bboxPerimeter = 2d * (width + height);

        var closed = usable
            .Select((points, index) => new ContourInfo(
                index,
                points,
                Bounds(points),
                Math.Abs(SignedArea(points)),
                Perimeter(points),
                PolygonCentroid(points)))
            .Where(x => x.Area > bboxArea * 1e-8)
            .ToArray();

        if (closed.Length == 0)
            return new DigitStructuralFeatures(contours.Count, 0, 0, 0, 0, width / height, 0, 0, Array.Empty<DigitHoleFeature>());

        var depth = new int[closed.Length];
        for (var i = 0; i < closed.Length; i++)
        {
            var sample = FindInteriorSample(closed[i].Points, closed[i].Centroid);
            for (var j = 0; j < closed.Length; j++)
            {
                if (i == j || closed[j].Area <= closed[i].Area)
                    continue;
                if (!BoxContains(closed[j].Bounds, sample))
                    continue;
                if (PointInPolygon(sample, closed[j].Points))
                    depth[i]++;
            }
        }

        // Even nesting depth is filled material; odd nesting depth is a hole.
        var holes = closed
            .Select((contour, index) => new { Contour = contour, Depth = depth[index] })
            .Where(x => x.Depth % 2 == 1)
            .OrderByDescending(x => x.Contour.Area)
            .Select(x => new DigitHoleFeature(
                CenterX: (x.Contour.Centroid.X - bounds.Left) / width,
                CenterY: (x.Contour.Centroid.Y - bounds.Top) / height,
                AreaRatio: x.Contour.Area / bboxArea))
            .ToArray();

        var signedFillArea = closed
            .Select((contour, index) => (depth[index] % 2 == 0 ? 1d : -1d) * contour.Area)
            .Sum();

        var fillRatio = Math.Clamp(signedFillArea / bboxArea, 0d, 1d);
        var normalizedPerimeter = bboxPerimeter <= 1e-12
            ? 0d
            : closed.Sum(x => x.Perimeter) / bboxPerimeter;

        return new DigitStructuralFeatures(
            RawContourCount: contours.Count,
            ClosedContourCount: closed.Length,
            OuterContourCount: depth.Count(x => x % 2 == 0),
            HoleCount: holes.Length,
            MaxNestingDepth: depth.Length == 0 ? 0 : depth.Max(),
            AspectRatio: width / height,
            FillRatio: fillRatio,
            NormalizedPerimeter: normalizedPerimeter,
            Holes: holes);
    }

    private static List<Vector2> CloseIfNeeded(List<Vector2> source)
    {
        var result = source.ToList();
        if (result.Count >= 3 && Vector2.DistanceSquared(result[0], result[^1]) > 1e-8f)
            result.Add(result[0]);
        return result;
    }

    private static Vector2 FindInteriorSample(IReadOnlyList<Vector2> polygon, Vector2 centroid)
    {
        if (PointInPolygon(centroid, polygon))
            return centroid;

        // For strongly concave paths the area centroid can fall outside. Try points halfway
        // from a boundary vertex toward the simple mean until one is inside.
        var mean = new Vector2(
            polygon.Average(x => x.X),
            polygon.Average(x => x.Y));

        foreach (var point in polygon.Take(Math.Max(1, polygon.Count - 1)))
        {
            var candidate = Vector2.Lerp(point, mean, 0.25f);
            if (PointInPolygon(candidate, polygon))
                return candidate;
        }

        return polygon[0];
    }

    private static Vector2 PolygonCentroid(IReadOnlyList<Vector2> points)
    {
        double twiceArea = 0;
        double cx = 0;
        double cy = 0;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var cross = a.X * b.Y - b.X * a.Y;
            twiceArea += cross;
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }

        if (Math.Abs(twiceArea) < 1e-10)
            return new Vector2(points.Average(x => x.X), points.Average(x => x.Y));

        return new Vector2(
            (float)(cx / (3d * twiceArea)),
            (float)(cy / (3d * twiceArea)));
    }

    private static double SignedArea(IReadOnlyList<Vector2> points)
    {
        double sum = 0;
        for (var i = 0; i < points.Count - 1; i++)
            sum += points[i].X * points[i + 1].Y - points[i + 1].X * points[i].Y;
        return sum / 2d;
    }

    private static double Perimeter(IReadOnlyList<Vector2> points)
    {
        double result = 0;
        for (var i = 1; i < points.Count; i++)
            result += Vector2.Distance(points[i - 1], points[i]);
        return result;
    }

    private static bool PointInPolygon(Vector2 p, IReadOnlyList<Vector2> polygon)
    {
        var inside = false;
        for (var i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            var denominator = pj.Y - pi.Y;
            if (Math.Abs(denominator) < 1e-12)
                denominator = denominator < 0 ? -1e-12f : 1e-12f;

            if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                p.X < (pj.X - pi.X) * (p.Y - pi.Y) / denominator + pi.X)
                inside = !inside;
        }
        return inside;
    }

    private static bool BoxContains(BoundsD bounds, Vector2 p) =>
        p.X >= bounds.Left && p.X <= bounds.Right && p.Y >= bounds.Top && p.Y <= bounds.Bottom;

    private static BoundsD Bounds(IEnumerable<Vector2> source)
    {
        var points = source.ToArray();
        return new BoundsD(
            points.Min(p => p.X),
            points.Min(p => p.Y),
            points.Max(p => p.X),
            points.Max(p => p.Y));
    }

    private static void ReadPicture(SKPicture picture, SKMatrix parentMatrix, ICollection<List<Vector2>> contours)
    {
        if (picture.Commands is null)
            return;

        var matrix = parentMatrix;
        var stack = new Stack<SKMatrix>();

        foreach (var command in picture.Commands)
        {
            switch (command)
            {
                case SaveCanvasCommand:
                case SaveLayerCanvasCommand:
                    stack.Push(matrix);
                    break;
                case RestoreCanvasCommand:
                    if (stack.Count > 0)
                        matrix = stack.Pop();
                    break;
                case SetMatrixCanvasCommand setMatrix:
                    matrix = parentMatrix.PreConcat(setMatrix.TotalMatrix);
                    break;
                case DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                    ReadPath(drawPath.Path, matrix, contours);
                    break;
                case DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    ReadPicture(drawPicture.Picture, matrix, contours);
                    break;
            }
        }
    }

    private static void ReadPath(SKPath path, SKMatrix matrix, ICollection<List<Vector2>> contours)
    {
        List<Vector2>? current = null;
        Vector2 cursor = default;
        Vector2 start = default;
        var hasCurrent = false;

        void Flush()
        {
            if (current is { Count: >= 2 })
                contours.Add(current);
            current = null;
            hasCurrent = false;
        }

        void Begin(Vector2 point)
        {
            Flush();
            current = new List<Vector2> { point };
            cursor = start = point;
            hasCurrent = true;
        }

        void Add(Vector2 point)
        {
            current ??= new List<Vector2>();
            if (current.Count == 0 || Vector2.DistanceSquared(current[^1], point) > 1e-10f)
                current.Add(point);
            cursor = point;
            hasCurrent = true;
        }

        foreach (var command in path)
        {
            switch (command)
            {
                case MoveToPathCommand move:
                    Begin(Map(matrix, move.X, move.Y));
                    break;
                case LineToPathCommand line when hasCurrent:
                    Add(Map(matrix, line.X, line.Y));
                    break;
                case QuadToPathCommand quad when hasCurrent:
                {
                    var p0 = cursor;
                    var p1 = Map(matrix, quad.X0, quad.Y0);
                    var p2 = Map(matrix, quad.X1, quad.Y1);
                    for (var i = 1; i <= CurveSteps; i++)
                    {
                        var t = i / (float)CurveSteps;
                        var mt = 1f - t;
                        Add(mt * mt * p0 + 2f * mt * t * p1 + t * t * p2);
                    }
                    break;
                }
                case CubicToPathCommand cubic when hasCurrent:
                {
                    var p0 = cursor;
                    var p1 = Map(matrix, cubic.X0, cubic.Y0);
                    var p2 = Map(matrix, cubic.X1, cubic.Y1);
                    var p3 = Map(matrix, cubic.X2, cubic.Y2);
                    for (var i = 1; i <= CurveSteps; i++)
                    {
                        var t = i / (float)CurveSteps;
                        var mt = 1f - t;
                        Add(mt * mt * mt * p0 + 3f * mt * mt * t * p1 + 3f * mt * t * t * p2 + t * t * t * p3);
                    }
                    break;
                }
                case ArcToPathCommand arc when hasCurrent:
                    Add(Map(matrix, arc.X, arc.Y));
                    break;
                case ClosePathCommand when hasCurrent:
                    Add(start);
                    Flush();
                    break;
                case AddPolyPathCommand poly:
                    Flush();
                    if (poly.Count > 0)
                    {
                        var points = Enumerable.Range(0, poly.Count)
                            .Select(i => Map(matrix, poly[i].X, poly[i].Y))
                            .ToList();
                        if (poly.Close && points.Count > 1)
                            points.Add(points[0]);
                        if (points.Count >= 2)
                            contours.Add(points);
                    }
                    break;
                case AddRectPathCommand rect:
                    Flush();
                    contours.Add(RectPoints(rect.Rect, matrix));
                    break;
                case AddRoundRectPathCommand roundRect:
                    Flush();
                    contours.Add(RectPoints(roundRect.Rect, matrix));
                    break;
                case AddCirclePathCommand circle:
                    Flush();
                    contours.Add(EllipsePoints(circle.X, circle.Y, circle.Radius, circle.Radius, matrix));
                    break;
                case AddOvalPathCommand oval:
                    Flush();
                    contours.Add(EllipsePoints(
                        (oval.Rect.Left + oval.Rect.Right) / 2f,
                        (oval.Rect.Top + oval.Rect.Bottom) / 2f,
                        oval.Rect.Width / 2f,
                        oval.Rect.Height / 2f,
                        matrix));
                    break;
            }
        }

        Flush();
    }

    private static List<Vector2> RectPoints(SKRect rect, SKMatrix matrix)
    {
        var result = new List<Vector2>
        {
            Map(matrix, rect.Left, rect.Top),
            Map(matrix, rect.Right, rect.Top),
            Map(matrix, rect.Right, rect.Bottom),
            Map(matrix, rect.Left, rect.Bottom)
        };
        result.Add(result[0]);
        return result;
    }

    private static List<Vector2> EllipsePoints(float cx, float cy, float rx, float ry, SKMatrix matrix)
    {
        const int steps = 64;
        var result = new List<Vector2>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var angle = 2d * Math.PI * i / steps;
            result.Add(Map(
                matrix,
                cx + rx * (float)Math.Cos(angle),
                cy + ry * (float)Math.Sin(angle)));
        }
        return result;
    }

    private static Vector2 Map(SKMatrix matrix, float x, float y)
    {
        var point = matrix.MapPoint(new SKPoint(x, y));
        return new Vector2(point.X, point.Y);
    }

    private sealed record ContourInfo(
        int Index,
        IReadOnlyList<Vector2> Points,
        BoundsD Bounds,
        double Area,
        double Perimeter,
        Vector2 Centroid);

    private sealed record BoundsD(double Left, double Top, double Right, double Bottom);
}
