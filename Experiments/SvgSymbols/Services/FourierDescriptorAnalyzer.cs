using System.Globalization;
using System.Numerics;
using ShimSkiaSharp;
using Svg.Skia;

namespace SvgSymbols.Services;

public sealed record FourierCoefficient(double Real, double Imag)
{
    public double Magnitude => Math.Sqrt(Real * Real + Imag * Imag);
}

public sealed record ContourFourierDescriptor(
    double Weight,
    double CenterX,
    double CenterY,
    double Width,
    double Height,
    IReadOnlyList<FourierCoefficient> Coefficients)
{
    public IReadOnlyList<double> Magnitudes => Coefficients.Select(x => x.Magnitude).ToArray();
}

public sealed record FourierDescriptor(
    int RawContourCount,
    int ContourCount,
    IReadOnlyList<ContourFourierDescriptor> Contours);

/// <summary>
/// Builds vector-only Fourier descriptors from SVG contours.
/// Translation is removed by discarding F0; scale is removed by normalizing total spectral energy.
/// Rotation is intentionally NOT normalized: music glyphs are expected to preserve orientation, and
/// retaining complex phase helps distinguish visually different shapes with similar Fourier magnitudes.
/// Start point and contour direction are canonicalized before DFT.
/// </summary>
public sealed class FourierDescriptorAnalyzer
{
    private const int CurveSteps = 16;
    private const int ResampleCount = 128;
    private const int CoefficientCount = 8;
    private const int MaxContours = 3;

    public FourierDescriptor Analyze(string svgPath)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var picture = svg.Model
            ?? throw new InvalidOperationException($"Svg.Skia did not produce a retained scene model for '{svgPath}'.");

        var rawContours = new List<List<Vector2>>();
        ReadPicture(picture, SKMatrix.Identity, rawContours);

        var rawUsable = rawContours
            .Where(x => x.Count >= 3)
            .Select(x => new { Points = x, Length = Length(x) })
            .Where(x => x.Length > 0.0001)
            .ToList();

        if (rawUsable.Count == 0)
            return new FourierDescriptor(0, 0, Array.Empty<ContourFourierDescriptor>());

        var prepared = rawUsable
            .Select(x => PrepareContour(x.Points, x.Length))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        // Svg.Skia can expose the same rendered contour more than once for some SVG structures.
        // Deduplicate only near-identical normalized geometry; do not merge merely similar contours.
        var unique = new List<PreparedContour>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contour in prepared.OrderByDescending(x => x.Length))
        {
            if (seen.Add(BuildGeometryKey(contour.CanonicalPoints)))
                unique.Add(contour);
        }

        if (unique.Count == 0)
            return new FourierDescriptor(rawUsable.Count, 0, Array.Empty<ContourFourierDescriptor>());

        var bounds = Bounds(unique.SelectMany(x => x.CanonicalPoints));
        var symbolWidth = Math.Max(bounds.Right - bounds.Left, 1e-9);
        var symbolHeight = Math.Max(bounds.Bottom - bounds.Top, 1e-9);
        var symbolScale = Math.Max(symbolWidth, symbolHeight);
        var totalLength = unique.Sum(x => x.Length);

        var descriptors = unique
            .OrderByDescending(x => x.Length)
            .Take(MaxContours)
            .Select(x => BuildDescriptor(x, bounds, symbolScale, totalLength))
            .ToArray();

        return new FourierDescriptor(rawUsable.Count, unique.Count, descriptors);
    }

    private static PreparedContour? PrepareContour(IReadOnlyList<Vector2> source, double length)
    {
        var sampled = ResampleClosed(source, ResampleCount);
        if (sampled.Length < 3)
            return null;

        var canonical = Canonicalize(sampled);
        return new PreparedContour(canonical, length);
    }

    private static ContourFourierDescriptor BuildDescriptor(
        PreparedContour contour,
        BoundsD symbolBounds,
        double symbolScale,
        double totalLength)
    {
        var points = contour.CanonicalPoints;
        var contourBounds = Bounds(points);
        var center = Centroid(points);
        var coefficients = Dft(points, CoefficientCount + 1)
            .Skip(1)
            .Take(CoefficientCount)
            .ToArray();

        var energy = Math.Sqrt(coefficients.Sum(x => x.Real * x.Real + x.Imaginary * x.Imaginary));
        if (energy <= 1e-12)
            energy = 1d;

        var normalized = coefficients
            .Select(x => new FourierCoefficient(x.Real / energy, x.Imaginary / energy))
            .ToArray();

        var symbolCenterX = (symbolBounds.Left + symbolBounds.Right) / 2d;
        var symbolCenterY = (symbolBounds.Top + symbolBounds.Bottom) / 2d;

        return new ContourFourierDescriptor(
            Weight: totalLength <= 1e-12 ? 0d : contour.Length / totalLength,
            CenterX: (center.X - symbolCenterX) / symbolScale,
            CenterY: (center.Y - symbolCenterY) / symbolScale,
            Width: (contourBounds.Right - contourBounds.Left) / symbolScale,
            Height: (contourBounds.Bottom - contourBounds.Top) / symbolScale,
            Coefficients: normalized);
    }

    private static Vector2[] Canonicalize(Vector2[] points)
    {
        var result = points.ToArray();

        // Force one traversal direction. This preserves orientation on the page while removing the
        // arbitrary clockwise/counter-clockwise choice made by SVG authors.
        if (SignedArea(result) < 0)
            Array.Reverse(result);

        // Choose a stable start point: top-most, then left-most. The tolerance avoids tiny Bezier
        // approximation noise changing the chosen point between visually identical outlines.
        var minY = result.Min(p => p.Y);
        var yTolerance = Math.Max(1e-5f, (result.Max(p => p.Y) - minY) * 1e-4f);
        var candidates = Enumerable.Range(0, result.Length)
            .Where(i => Math.Abs(result[i].Y - minY) <= yTolerance)
            .ToArray();
        var start = candidates.OrderBy(i => result[i].X).First();

        if (start == 0)
            return result;

        var rotated = new Vector2[result.Length];
        for (var i = 0; i < result.Length; i++)
            rotated[i] = result[(start + i) % result.Length];
        return rotated;
    }

    private static string BuildGeometryKey(IReadOnlyList<Vector2> points)
    {
        var center = Centroid(points);
        var bounds = Bounds(points);
        var scale = Math.Max(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        if (scale <= 1e-12)
            scale = 1d;

        // 32 evenly spaced canonical samples are plenty for detecting exact/near-exact duplicate paths.
        var parts = new string[32];
        for (var i = 0; i < parts.Length; i++)
        {
            var p = points[i * points.Count / parts.Length];
            var x = (p.X - center.X) / scale;
            var y = (p.Y - center.Y) / scale;
            parts[i] = $"{x.ToString("0.0000", CultureInfo.InvariantCulture)},{y.ToString("0.0000", CultureInfo.InvariantCulture)}";
        }
        return string.Join(';', parts);
    }

    private static double SignedArea(IReadOnlyList<Vector2> points)
    {
        double sum = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2d;
    }

    private static Vector2 Centroid(IReadOnlyList<Vector2> points)
    {
        double x = 0, y = 0;
        foreach (var p in points)
        {
            x += p.X;
            y += p.Y;
        }
        return new Vector2((float)(x / points.Count), (float)(y / points.Count));
    }

    private static BoundsD Bounds(IEnumerable<Vector2> source)
    {
        var points = source.ToArray();
        return new BoundsD(
            points.Min(p => p.X),
            points.Min(p => p.Y),
            points.Max(p => p.X),
            points.Max(p => p.Y));
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

    private static void ReadPath(SKPath path, SKMatrix matrix, ICollection<List<Vector2>> contours)
    {
        List<Vector2>? currentContour = null;
        Vector2 current = default;
        Vector2 start = default;
        var hasCurrent = false;

        void Flush()
        {
            if (currentContour is { Count: >= 2 })
                contours.Add(currentContour);
            currentContour = null;
            hasCurrent = false;
        }

        void StartContour(Vector2 point)
        {
            Flush();
            currentContour = new List<Vector2> { point };
            current = start = point;
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
            Map(matrix, rect.Left, rect.Top), Map(matrix, rect.Right, rect.Top),
            Map(matrix, rect.Right, rect.Bottom), Map(matrix, rect.Left, rect.Bottom)
        };
        result.Add(result[0]);
        return result;
    }

    private static List<Vector2> EllipsePoints(float cx, float cy, float rx, float ry, SKMatrix matrix)
    {
        const int steps = 48;
        var result = new List<Vector2>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var a = 2d * Math.PI * i / steps;
            result.Add(Map(matrix, cx + rx * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a)));
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
        if (total <= 1e-12)
            return Array.Empty<Vector2>();

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
            var t = segmentLength <= 1e-12 ? 0f : (float)((target - cumulative[segment - 1]) / segmentLength);
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

    private sealed record PreparedContour(Vector2[] CanonicalPoints, double Length);
    private sealed record BoundsD(double Left, double Top, double Right, double Bottom);
}
