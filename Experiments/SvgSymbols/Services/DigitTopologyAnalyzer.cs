using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ShimSkiaSharp;
using Svg.Skia;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed record DigitCandidateScore(int Digit, double Distance, double Probability);

public sealed record DigitRecognition(
    int Index,
    int Digit,
    double Probability,
    IReadOnlyList<DigitCandidateScore> Candidates);

public sealed record NumberRecognition(
    string? Value,
    double Probability,
    int SegmentCount,
    IReadOnlyList<DigitRecognition> Digits,
    string? Error = null);

/// <summary>
/// Third experimental recognition mode dedicated to time-signature digits.
/// It splits a symbol into at most two digits using a real vertical whitespace corridor,
/// then compares every digit against the known single-digit corpus using the full current
/// vector descriptor (scanlines + geometry + Fourier), with extra topology penalties.
/// No rasterization and no OCR are used.
/// </summary>
public sealed class DigitTopologyAnalyzer
{
    private const int CurveSteps = 16;
    private const double Temperature = 0.55;
    private const double MinimumSplitGapOfHeight = 0.03;
    private const double FirstDigitOnePriorPenalty = 2.0;

    private static readonly Regex DigitFileName = new(
        @"^(?:Music|Bravura-)(?<digit>[0-9])\.svg$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly FourierDescriptorAnalyzer _descriptorAnalyzer = new();
    private readonly FourierDescriptorComparer _descriptorComparer = new();

    public NumberRecognition Analyze(
        string svgPath,
        IReadOnlyList<(SymbolSource Source, string Path)> rhythmCorpus,
        string? excludeFileName = null)
    {
        try
        {
            var references = BuildReferences(rhythmCorpus, excludeFileName);
            if (references.Count == 0)
                return new NumberRecognition(null, 0, 0, Array.Empty<DigitRecognition>(), "No single-digit references.");

            var contours = ExtractContours(svgPath);
            if (contours.Count == 0)
                return new NumberRecognition(null, 0, 0, Array.Empty<DigitRecognition>(), "No usable contours.");

            var segments = SplitIntoDigits(contours);
            if (segments.Count == 0)
                return new NumberRecognition(null, 0, 0, Array.Empty<DigitRecognition>(), "Could not split into digit candidates.");

            var recognized = new List<DigitRecognition>();
            for (var i = 0; i < segments.Count; i++)
            {
                using var temporary = TemporaryVectorGlyph.Create(segments[i]);
                var descriptor = _descriptorAnalyzer.Analyze(temporary.Path);
                var topology = BuildTopology(segments[i]);

                var byDigit = references
                    .GroupBy(x => x.Digit)
                    .Select(group =>
                    {
                        var distance = group.Min(reference =>
                            CombinedDistance(descriptor, topology, reference.Descriptor, reference.Topology));

                        // Compound time-signature numbers overwhelmingly start with 1 (10..19).
                        // Keep this as a strong prior rather than a hard rule so 32 can still win
                        // when its geometry is clearly much closer to 3 than to 1.
                        if (segments.Count == 2 && i == 0 && group.Key != 1)
                            distance += FirstDigitOnePriorPenalty;

                        return new { Digit = group.Key, Distance = distance };
                    })
                    .OrderBy(x => x.Distance)
                    .ToArray();

                var probabilities = SoftmaxProbabilities(byDigit.Select(x => x.Distance).ToArray());
                var bestDistance = byDigit[0].Distance;

                var absoluteQuality = Math.Exp(-bestDistance / 3.0);
                var candidates = byDigit
                    .Select((x, index) => new DigitCandidateScore(
                        x.Digit,
                        x.Distance,
                        Math.Clamp(probabilities[index] * absoluteQuality, 0, 1)))
                    .OrderByDescending(x => x.Probability)
                    .Take(3)
                    .ToArray();

                var best = candidates[0];
                recognized.Add(new DigitRecognition(i, best.Digit, best.Probability, candidates));
            }

            var value = string.Concat(recognized.Select(x => x.Digit.ToString(CultureInfo.InvariantCulture)));
            var probability = recognized.Count == 0
                ? 0d
                : Math.Pow(recognized.Select(x => Math.Max(x.Probability, 1e-9)).Aggregate(1d, (a, b) => a * b), 1d / recognized.Count);

            return new NumberRecognition(value, probability, segments.Count, recognized);
        }
        catch (Exception ex)
        {
            return new NumberRecognition(null, 0, 0, Array.Empty<DigitRecognition>(), ex.Message);
        }
    }

    private List<ReferenceDigit> BuildReferences(
        IReadOnlyList<(SymbolSource Source, string Path)> corpus,
        string? excludeFileName)
    {
        var result = new List<ReferenceDigit>();

        foreach (var item in corpus)
        {
            if (!string.IsNullOrWhiteSpace(excludeFileName) &&
                string.Equals(item.Source.FileName, excludeFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            var match = DigitFileName.Match(Path.GetFileName(item.Source.FileName));
            if (!match.Success)
                continue;

            var digit = int.Parse(match.Groups["digit"].Value, CultureInfo.InvariantCulture);
            var contours = ExtractContours(item.Path);
            if (contours.Count == 0)
                continue;

            result.Add(new ReferenceDigit(
                digit,
                _descriptorAnalyzer.Analyze(item.Path),
                BuildTopology(contours)));
        }

        return result;
    }

    private double CombinedDistance(
        FourierDescriptor a,
        DigitTopology aTopology,
        FourierDescriptor b,
        DigitTopology bTopology)
    {
        var distance = _descriptorComparer.ComplexDistance(a, b);
        distance += 1.35 * Square(aTopology.HoleCount - bTopology.HoleCount);
        distance += 0.45 * Square(aTopology.AspectRatio - bTopology.AspectRatio);
        distance += 0.08 * Square(Math.Min(aTopology.ContourCount, 8) - Math.Min(bTopology.ContourCount, 8));
        return distance;
    }

    private static double[] SoftmaxProbabilities(IReadOnlyList<double> distances)
    {
        if (distances.Count == 0)
            return Array.Empty<double>();

        var min = distances.Min();
        var weights = distances.Select(x => Math.Exp(-(x - min) / Temperature)).ToArray();
        var total = weights.Sum();
        return total <= 1e-12
            ? Enumerable.Repeat(1d / weights.Length, weights.Length).ToArray()
            : weights.Select(x => x / total).ToArray();
    }

    private static DigitTopology BuildTopology(IReadOnlyList<List<Vector2>> contours)
    {
        var bounds = Bounds(contours.SelectMany(x => x));
        var width = Math.Max(bounds.Right - bounds.Left, 1e-9);
        var height = Math.Max(bounds.Bottom - bounds.Top, 1e-9);

        var closed = contours
            .Where(IsClosed)
            .Select(x => new ContourBox(x, Bounds(x)))
            .ToArray();

        var holes = 0;
        for (var i = 0; i < closed.Length; i++)
        {
            var sample = Centroid(closed[i].Points);
            for (var j = 0; j < closed.Length; j++)
            {
                if (i == j)
                    continue;
                if (BoxContains(closed[j].Bounds, sample) && PointInPolygon(sample, closed[j].Points))
                {
                    holes++;
                    break;
                }
            }
        }

        return new DigitTopology(contours.Count, holes, width / height);
    }

    private static IReadOnlyList<IReadOnlyList<List<Vector2>>> SplitIntoDigits(IReadOnlyList<List<Vector2>> contours)
    {
        var usable = contours.Where(x => x.Count >= 3).ToArray();
        if (usable.Length == 0)
            return Array.Empty<IReadOnlyList<List<Vector2>>>();

        var symbolBounds = Bounds(usable.SelectMany(x => x));
        var symbolHeight = Math.Max(symbolBounds.Bottom - symbolBounds.Top, 1e-9);
        var minimumGap = symbolHeight * MinimumSplitGapOfHeight;

        // Project every contour's occupied X range onto one axis and merge all overlapping ranges.
        // A split is allowed only through a vertical corridor that is empty for the whole symbol,
        // not merely through a gap between two individual SVG paths of the same visual digit.
        var occupied = usable
            .Select(Bounds)
            .Select(x => new Interval(x.Left, x.Right))
            .OrderBy(x => x.Left)
            .ToArray();

        var merged = new List<Interval>();
        foreach (var interval in occupied)
        {
            if (merged.Count == 0 || interval.Left > merged[^1].Right)
            {
                merged.Add(interval);
                continue;
            }

            merged[^1] = merged[^1] with { Right = Math.Max(merged[^1].Right, interval.Right) };
        }

        var gaps = Enumerable.Range(0, Math.Max(0, merged.Count - 1))
            .Select(i => new
            {
                Left = merged[i].Right,
                Right = merged[i + 1].Left,
                Width = merged[i + 1].Left - merged[i].Right
            })
            .Where(x => x.Width >= minimumGap)
            .OrderByDescending(x => x.Width)
            .ToArray();

        // No convincing full-height whitespace corridor: treat the glyph as a single digit.
        if (gaps.Length == 0)
            return [usable];

        // At most two digits are meaningful for our rhythm-number corpus. Use only the strongest cut.
        var cut = (gaps[0].Left + gaps[0].Right) / 2d;
        var left = new List<List<Vector2>>();
        var right = new List<List<Vector2>>();

        foreach (var contour in usable)
        {
            var bounds = Bounds(contour);
            var centerX = (bounds.Left + bounds.Right) / 2d;
            (centerX < cut ? left : right).Add(contour);
        }

        if (left.Count == 0 || right.Count == 0)
            return [usable];

        return [left, right];
    }

    private static List<List<Vector2>> ExtractContours(string svgPath)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var picture = svg.Model
            ?? throw new InvalidOperationException($"Svg.Skia did not produce a retained scene model for '{svgPath}'.");

        var result = new List<List<Vector2>>();
        ReadPicture(picture, SKMatrix.Identity, result);
        return result.Where(x => x.Count >= 3).ToList();
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
                    if (stack.Count > 0) matrix = stack.Pop();
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
            if (current is { Count: >= 2 }) contours.Add(current);
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
                        var points = Enumerable.Range(0, poly.Count).Select(i => Map(matrix, poly[i].X, poly[i].Y)).ToList();
                        if (poly.Close && points.Count > 1) points.Add(points[0]);
                        if (points.Count >= 2) contours.Add(points);
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
                    contours.Add(EllipsePoints((oval.Rect.Left + oval.Rect.Right) / 2f, (oval.Rect.Top + oval.Rect.Bottom) / 2f, oval.Rect.Width / 2f, oval.Rect.Height / 2f, matrix));
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

    private static bool IsClosed(IReadOnlyList<Vector2> points) =>
        points.Count >= 4 && Vector2.DistanceSquared(points[0], points[^1]) < 1e-6f;

    private static bool PointInPolygon(Vector2 p, IReadOnlyList<Vector2> polygon)
    {
        var inside = false;
        for (var i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                p.X < (pj.X - pi.X) * (p.Y - pi.Y) / Math.Max(pj.Y - pi.Y, 1e-12f) + pi.X)
                inside = !inside;
        }
        return inside;
    }

    private static bool BoxContains(BoundsD bounds, Vector2 p) =>
        p.X >= bounds.Left && p.X <= bounds.Right && p.Y >= bounds.Top && p.Y <= bounds.Bottom;

    private static Vector2 Centroid(IReadOnlyList<Vector2> points)
    {
        double x = 0, y = 0;
        foreach (var p in points) { x += p.X; y += p.Y; }
        return new Vector2((float)(x / points.Count), (float)(y / points.Count));
    }

    private static BoundsD Bounds(IEnumerable<Vector2> source)
    {
        var points = source.ToArray();
        return new BoundsD(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    private static double Square(double value) => value * value;

    private sealed record ReferenceDigit(int Digit, FourierDescriptor Descriptor, DigitTopology Topology);
    private sealed record DigitTopology(int ContourCount, int HoleCount, double AspectRatio);
    private sealed record ContourBox(List<Vector2> Points, BoundsD Bounds);
    private sealed record BoundsD(double Left, double Top, double Right, double Bottom);
    private sealed record Interval(double Left, double Right);

    private sealed class TemporaryVectorGlyph : IDisposable
    {
        private TemporaryVectorGlyph(string path) => Path = path;
        public string Path { get; }

        public static TemporaryVectorGlyph Create(IReadOnlyList<List<Vector2>> contours)
        {
            var bounds = Bounds(contours.SelectMany(x => x));
            var width = Math.Max(bounds.Right - bounds.Left, 1e-6);
            var height = Math.Max(bounds.Bottom - bounds.Top, 1e-6);
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"svgsymbols-digit-{Guid.NewGuid():N}.svg");

            var svg = new StringBuilder();
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
                .Append(Fmt(bounds.Left)).Append(' ').Append(Fmt(bounds.Top)).Append(' ')
                .Append(Fmt(width)).Append(' ').Append(Fmt(height)).Append("\">");

            foreach (var contour in contours)
            {
                svg.Append("<polygon points=\"");
                foreach (var p in contour)
                    svg.Append(Fmt(p.X)).Append(',').Append(Fmt(p.Y)).Append(' ');
                svg.Append("\"/>");
            }
            svg.Append("</svg>");

            File.WriteAllText(path, svg.ToString());
            return new TemporaryVectorGlyph(path);
        }

        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch { }
        }

        private static string Fmt(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
