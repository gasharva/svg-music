using System.Numerics;
using ShimSkiaSharp;
using Svg.Skia;

namespace SvgSymbols.Services;

public sealed record FourierDescriptor(
    int ContourCount,
    IReadOnlyList<double> Magnitudes);

/// <summary>
/// Builds a small translation/scale/rotation/start-point invariant descriptor directly
/// from SVG vector contours. No rasterization is involved.
///
/// For this first experiment the DFT is calculated for the longest contour of the symbol.
/// Separate dots/holes/components are intentionally not folded into the DFT yet; ContourCount
/// is reported alongside the descriptor so we can see where that limitation matters.
/// </summary>
public sealed class FourierDescriptorAnalyzer
{
    private const int CurveSteps = 16;
    private const int ResampleCount = 128;
    private const int CoefficientCount = 6;

    public FourierDescriptor Analyze(string svgPath)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var picture = svg.Model
            ?? throw new InvalidOperationException($"Svg.Skia did not produce a retained scene model for '{svgPath}'.");

        var contours = new List<List<Vector2>>();
        ReadPicture(picture, SKMatrix.Identity, contours);

        var usable = contours
            .Where(x => x.Count >= 3)
            .Select(x => new { Points = x, Length = Length(x) })
            .Where(x => x.Length > 0.0001)
            .OrderByDescending(x => x.Length)
            .ToList();

        if (usable.Count == 0)
            return new FourierDescriptor(0, Array.Empty<double>());

        var sampled = ResampleClosed(usable[0].Points, ResampleCount);
        var coefficients = Dft(sampled, CoefficientCount + 1);

        // k=0 is the centroid/DC component. Ignore it for translation invariance.
        // Divide by the first non-zero harmonic for scale invariance.
        // Magnitudes are rotation- and start-point invariant as well.
        var scale = coefficients
            .Skip(1)
            .Select(Complex.Abs)
            .FirstOrDefault(x => x > 1e-12);

        if (scale <= 1e-12)
            return new FourierDescriptor(usable.Count, Enumerable.Repeat(0d, CoefficientCount).ToArray());

        var magnitudes = coefficients
            .Skip(1)
            .Take(CoefficientCount)
            .Select(x => Complex.Abs(x) / scale)
            .ToArray();

        return new FourierDescriptor(usable.Count, magnitudes);
    }

    private static void ReadPicture(
        SKPicture picture,
        SKMatrix parentMatrix,
        ICollection<List<Vector2>> contours)
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

    private static void ReadPath(
        SKPath path,
        SKMatrix matrix,
        ICollection<List<Vector2>> contours)
    {
        List<Vector2>? currentContour = null;
        Vector2 current = default;
        Vector2 start = default;
        var hasCurrent = false;

        void StartContour(Vector2 point)
        {
            Flush();
            currentContour = new List<Vector2> { point };
            current = point;
            start = point;
            hasCurrent = true;
        }

        void Add(Vector2 point)
        {
            currentContour ??= new List<Vector2>();
            if (currentContour.Count == 0 || Vector2.DistanceSquared(currentContour[^1], point) > 1e-10f)
                currentContour.Add(point);
            current = point;
            hasCurrent = true;
        }

        void Flush()
        {
            if (currentContour is { Count: >= 2 })
                contours.Add(currentContour);
            currentContour = null;
            hasCurrent = false;
        }

        foreach (var command in path)
        {
            switch (command)
            {
                case MoveToPathCommand move:
                    StartContour(Map(matrix, move.X, move.Y));
                    break;

                case LineToPathCommand line when hasCurrent:
                    Add(Map(matrix, line.X, line.Y));
                    break;

                case QuadToPathCommand quad when hasCurrent:
                {
                    var p0 = current;
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
                    var p0 = current;
                    var p1 = Map(matrix, cubic.X0, cubic.Y0);
                    var p2 = Map(matrix, cubic.X1, cubic.Y1);
                    var p3 = Map(matrix, cubic.X2, cubic.Y2);
                    for (var i = 1; i <= CurveSteps; i++)
                    {
                        var t = i / (float)CurveSteps;
                        var mt = 1f - t;
                        Add(mt * mt * mt * p0
                            + 3f * mt * mt * t * p1
                            + 3f * mt * t * t * p2
                            + t * t * t * p3);
                    }
                    break;
                }

                case ArcToPathCommand arc when hasCurrent:
                    // Proper elliptical-arc flattening can be added if it proves important.
                    // For the current corpus clef outlines are overwhelmingly Bezier paths.
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
                    // Bounding rectangle is enough for the rare standalone round-rect glyph here.
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

    private static List<Vector2> EllipsePoints(
        float cx,
        float cy,
        float rx,
        float ry,
        SKMatrix matrix)
    {
        const int steps = 48;
        var result = new List<Vector2>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var a = 2d * Math.PI * i / steps;
            result.Add(Map(
                matrix,
                cx + rx * (float)Math.Cos(a),
                cy + ry * (float)Math.Sin(a)));
        }
        return result;
    }

    private static Vector2 Map(SKMatrix matrix, float x, float y)
    {
        var point = matrix.MapPoint(new SKPoint(x, y));
        return new Vector2(point.X, point.Y);
    }

    private static double Length(IReadOnlyList<Vector2> points)
    {
        double length = 0;
        for (var i = 1; i < points.Count; i++)
            length += Vector2.Distance(points[i - 1], points[i]);
        return length;
    }

    private static Vector2[] ResampleClosed(IReadOnlyList<Vector2> source, int count)
    {
        var points = source.ToList();
        if (Vector2.DistanceSquared(points[0], points[^1]) > 1e-10f)
            points.Add(points[0]);

        var cumulative = new double[points.Count];
        for (var i = 1; i < points.Count; i++)
            cumulative[i] = cumulative[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        var total = cumulative[^1];
        var result = new Vector2[count];
        var segment = 1;

        for (var i = 0; i < count; i++)
        {
            var target = total * i / count;
            while (segment < cumulative.Length - 1 && cumulative[segment] < target)
                segment++;

            var a = points[segment - 1];
            var b = points[segment];
            var segmentLength = cumulative[segment] - cumulative[segment - 1];
            var t = segmentLength <= 1e-12
                ? 0f
                : (float)((target - cumulative[segment - 1]) / segmentLength);
            result[i] = Vector2.Lerp(a, b, t);
        }

        return result;
    }

    private static Complex[] Dft(IReadOnlyList<Vector2> points, int coefficientCount)
    {
        var n = points.Count;
        var result = new Complex[coefficientCount];

        for (var k = 0; k < coefficientCount; k++)
        {
            Complex sum = Complex.Zero;
            for (var j = 0; j < n; j++)
            {
                var z = new Complex(points[j].X, points[j].Y);
                var angle = -2d * Math.PI * k * j / n;
                sum += z * Complex.FromPolarCoordinates(1d, angle);
            }
            result[k] = sum / n;
        }

        return result;
    }
}
