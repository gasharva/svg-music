using System.Numerics;
using System.Text;
using Svg.Skia;
using SvgStructure.Models;
using SvgSymbols.Services;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

/// <summary>
/// Resolves conventional numeric time signatures from MusicSymbolResolver candidates.
/// Candidate grouping and placement come from the pipeline; recognition consumes only the
/// recovered smooth Bezier geometry carried by MusicSymbolCandidate.
/// </summary>
public sealed class MeterResolver
{
    private const int CurveSteps = 20;

    private static readonly HashSet<(int Beats, int Value)> SupportedMeters = new()
    {
        (2, 2), (2, 4), (2, 8),
        (3, 2), (3, 4), (3, 8),
        (4, 2), (4, 4), (4, 8),
        (5, 4), (5, 8),
        (6, 4), (6, 8),
        (7, 4), (7, 8),
        (9, 8), (9, 16),
        (12, 8), (12, 16)
    };

    private readonly ISvgNumberRecognizer _numberRecognizer;

    public MeterResolver(ISvgNumberRecognizer numberRecognizer) =>
        _numberRecognizer = numberRecognizer;

    public MeterResolution? Resolve(PartMeasureBlock block, MusicSymbolResolution symbols)
    {
        var available = symbols.Candidates
            .Where(x =>
                x.Scope == PrimitiveLogicalScope.PartMeasure &&
                x.PartNumber == block.PartNumber &&
                x.MeasureNumber == block.MeasureNumber ||
                x.Scope == PrimitiveLogicalScope.Measure &&
                x.MeasureNumber == block.MeasureNumber)
            .Where(x => x.PhysicalBounds.IntersectsHorizontally(
                block.PhysicalBounds.Left,
                block.PhysicalBounds.Right))
            .Where(x => x.SmoothPaths.Count > 0)
            .ToArray();

        if (available.Length < 2 || block.PhysicalBounds.Height <= 0)
            return null;

        var candidates = BuildCandidates(block, available)
            .OrderByDescending(x => x.GeometryScore)
            .Take(8)
            .ToArray();

        var recognized = new List<ScoredMeter>();
        foreach (var candidate in candidates)
        {
            var meter = RecognizeCandidate(block, candidate);
            if (meter is not null)
                recognized.Add(meter);
        }

        return recognized
            .OrderByDescending(x => x.Score)
            .Select(x => x.Meter)
            .FirstOrDefault();
    }

    private static IReadOnlyList<MeterCandidate> BuildCandidates(
        PartMeasureBlock block,
        IReadOnlyList<MusicSymbolCandidate> symbols)
    {
        var b = block.PhysicalBounds;
        var staffHeight = b.Height;
        var middleY = b.CenterY;

        var rowSymbols = symbols
            .Where(x => x.PhysicalBounds.Height >= staffHeight * 0.22)
            .Where(x => x.PhysicalBounds.Height <= staffHeight * 0.72)
            .Where(x => x.PhysicalBounds.Width <= staffHeight * 1.15)
            .Where(x => x.PhysicalBounds.Width >= staffHeight * 0.10)
            .Where(x => x.PhysicalBounds.Width / Math.Max(1e-9, x.PhysicalBounds.Height) >= 0.20)
            .ToArray();

        var upper = rowSymbols.Where(x => x.PhysicalBounds.CenterY < middleY).ToArray();
        var lower = rowSymbols.Where(x => x.PhysicalBounds.CenterY >= middleY).ToArray();

        var upperClusters = BuildRowClusters(upper, staffHeight);
        var lowerClusters = BuildRowClusters(lower, staffHeight);
        var result = new List<MeterCandidate>();

        foreach (var top in upperClusters)
        {
            foreach (var bottom in lowerClusters)
            {
                var xOverlap = HorizontalOverlapRatio(top.Bounds, bottom.Bounds);
                if (xOverlap < 0.58)
                    continue;

                var centerDelta = Math.Abs(top.Bounds.CenterX - bottom.Bounds.CenterX) / staffHeight;
                if (centerDelta > 0.22)
                    continue;

                var widthRatio = Ratio(top.Bounds.Width, bottom.Bounds.Width);
                var heightRatio = Ratio(top.Bounds.Height, bottom.Bounds.Height);
                if (widthRatio < 0.48 || heightRatio < 0.52)
                    continue;

                var averageRowWidth = (top.Bounds.Width + bottom.Bounds.Width) / 2d;
                if (averageRowWidth < staffHeight * 0.14)
                    continue;

                var verticalGap = bottom.Bounds.Top - top.Bounds.Bottom;
                if (verticalGap > staffHeight * 0.24 || verticalGap < -staffHeight * 0.18)
                    continue;

                var total = Union(top.Bounds, bottom.Bounds);
                var verticalCoverage = total.Height / staffHeight;
                if (verticalCoverage < 0.72 || verticalCoverage > 1.35)
                    continue;

                var side = ResolveSide(block, total);
                if (side is null)
                    continue;

                var geometryScore =
                    1.8 * xOverlap +
                    0.65 * widthRatio +
                    0.35 * heightRatio +
                    0.35 * Math.Min(verticalCoverage, 1.05) -
                    0.55 * centerDelta;

                result.Add(new MeterCandidate(
                    side.Value,
                    top,
                    bottom,
                    total,
                    geometryScore));
            }
        }

        return result
            .OrderByDescending(x => x.GeometryScore)
            .GroupBy(x => (
                Side: x.Side,
                X: Math.Round(x.Bounds.CenterX / Math.Max(1, staffHeight * 0.12)),
                W: Math.Round(x.Bounds.Width / Math.Max(1, staffHeight * 0.12))))
            .Select(x => x.First())
            .ToArray();
    }

    private static IReadOnlyList<RowCluster> BuildRowClusters(
        IReadOnlyList<MusicSymbolCandidate> symbols,
        double staffHeight)
    {
        if (symbols.Count == 0)
            return Array.Empty<RowCluster>();

        var ordered = symbols.OrderBy(x => x.PhysicalBounds.Left).ToArray();
        var result = new List<RowCluster>();

        foreach (var symbol in ordered)
            result.Add(new RowCluster(new[] { symbol }, symbol.PhysicalBounds));

        // Compound meter numbers (12, 16) may still arrive as adjacent symbol candidates.
        for (var i = 0; i < ordered.Length - 1; i++)
        {
            var first = ordered[i];
            var second = ordered[i + 1];
            var gap = second.PhysicalBounds.Left - first.PhysicalBounds.Right;
            if (gap < -staffHeight * 0.10 || gap > staffHeight * 0.32)
                continue;

            var yOverlap = VerticalOverlapRatio(first.PhysicalBounds, second.PhysicalBounds);
            if (yOverlap < 0.55)
                continue;

            result.Add(new RowCluster(
                new[] { first, second },
                Union(first.PhysicalBounds, second.PhysicalBounds)));
        }

        return result;
    }

    private ScoredMeter? RecognizeCandidate(PartMeasureBlock block, MeterCandidate candidate)
    {
        var topContours = ToContours(candidate.Top.Symbols);
        var bottomContours = ToContours(candidate.Bottom.Symbols);
        if (topContours.Count == 0 || bottomContours.Count == 0)
            return null;

        var top = _numberRecognizer.Recognize(topContours);
        var bottom = _numberRecognizer.Recognize(bottomContours);

        var pair = BestSupportedPair(top, bottom);
        if (pair is null)
            return null;

        var confidence = Math.Sqrt(pair.Value.TopConfidence * pair.Value.BottomConfidence);

        return new ScoredMeter(
            new MeterResolution(
                block.PartNumber,
                block.MeasureNumber,
                pair.Value.Beats,
                pair.Value.Value,
                candidate.Side,
                confidence,
                candidate.Bounds,
                candidate.Top.Bounds,
                candidate.Bottom.Bounds),
            confidence + 0.18 * candidate.GeometryScore);
    }

    /// <summary>
    /// Converts the candidate's retained smooth SVG paths to contours only at the recognizer boundary.
    /// The MusicSymbolCandidate itself remains Bezier-based; this flattening exists solely because the
    /// current experimental number-recognizer interface consumes point contours.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<Vector2>> ToContours(
        IEnumerable<MusicSymbolCandidate> symbols)
    {
        var paths = symbols
            .SelectMany(x => x.SmoothPaths)
            .DistinctBy(x => $"{x.SourceAddress}\n{x.PathData}\n{x.Transform}", StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
            return Array.Empty<IReadOnlyList<Vector2>>();

        var tempPath = Path.Combine(Path.GetTempPath(), $"meter-symbol-{Guid.NewGuid():N}.svg");
        try
        {
            WriteSmoothSvg(paths, tempPath);
            using var svg = SKSvg.CreateFromFile(tempPath);
            var picture = svg.Model;
            if (picture is null)
                return Array.Empty<IReadOnlyList<Vector2>>();

            var contours = new List<List<Vector2>>();
            ReadPicture(picture, Shim.SKMatrix.Identity, contours);
            return contours.Cast<IReadOnlyList<Vector2>>().ToArray();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }
        }
    }

    private static void WriteSmoothSvg(IReadOnlyList<SmoothSvgPath> paths, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" overflow=\"visible\">");
        foreach (var path in paths)
        {
            var transform = string.IsNullOrWhiteSpace(path.Transform)
                ? string.Empty
                : $" transform=\"{System.Net.WebUtility.HtmlEncode(path.Transform)}\"";
            sb.Append("<path fill=\"black\" fill-rule=\"evenodd\"")
                .Append(transform)
                .Append(" d=\"")
                .Append(System.Net.WebUtility.HtmlEncode(path.PathData))
                .AppendLine("\"/>");
        }
        sb.AppendLine("</svg>");
        File.WriteAllText(outputPath, sb.ToString());
    }

    private static void ReadPicture(
        Shim.SKPicture picture,
        Shim.SKMatrix parentMatrix,
        ICollection<List<Vector2>> contours)
    {
        if (picture.Commands is null)
            return;

        var matrix = parentMatrix;
        var stack = new Stack<Shim.SKMatrix>();
        foreach (var command in picture.Commands)
        {
            switch (command)
            {
                case Shim.SaveCanvasCommand:
                case Shim.SaveLayerCanvasCommand:
                    stack.Push(matrix);
                    break;
                case Shim.RestoreCanvasCommand:
                    if (stack.Count > 0)
                        matrix = stack.Pop();
                    break;
                case Shim.SetMatrixCanvasCommand setMatrix:
                    matrix = parentMatrix.PreConcat(setMatrix.TotalMatrix);
                    break;
                case Shim.DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                    ReadPath(drawPath.Path, matrix, contours);
                    break;
                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    ReadPicture(drawPicture.Picture, matrix, contours);
                    break;
            }
        }
    }

    private static void ReadPath(
        Shim.SKPath path,
        Shim.SKMatrix matrix,
        ICollection<List<Vector2>> contours)
    {
        List<Vector2>? contour = null;
        Vector2 current = default;
        var hasCurrent = false;

        void Flush()
        {
            if (contour is { Count: >= 3 })
                contours.Add(contour);
            contour = null;
            hasCurrent = false;
        }

        void Start(Vector2 point)
        {
            Flush();
            contour = new List<Vector2> { point };
            current = point;
            hasCurrent = true;
        }

        void Add(Vector2 point)
        {
            contour ??= new List<Vector2>();
            if (contour.Count == 0 || Vector2.DistanceSquared(contour[^1], point) > 1e-10f)
                contour.Add(point);
            current = point;
            hasCurrent = true;
        }

        foreach (var command in path)
        {
            switch (command)
            {
                case Shim.MoveToPathCommand move:
                    Start(Map(matrix, move.X, move.Y));
                    break;
                case Shim.LineToPathCommand line when hasCurrent:
                    Add(Map(matrix, line.X, line.Y));
                    break;
                case Shim.QuadToPathCommand quad when hasCurrent:
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
                case Shim.CubicToPathCommand cubic when hasCurrent:
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
                case Shim.ClosePathCommand:
                    Flush();
                    break;
            }
        }
        Flush();
    }

    private static Vector2 Map(Shim.SKMatrix matrix, float x, float y)
    {
        var point = matrix.MapPoint(new Shim.SKPoint(x, y));
        return new Vector2(point.X, point.Y);
    }

    private static MeterSide? ResolveSide(PartMeasureBlock block, RectD bounds)
    {
        var b = block.PhysicalBounds;
        var localCenter = (bounds.CenterX - b.Left) / Math.Max(1e-9, b.Width);

        if (localCenter <= 0.48)
            return MeterSide.Left;
        if (localCenter >= 0.72)
            return MeterSide.Right;
        return null;
    }

    private static (int Beats, int Value, double TopConfidence, double BottomConfidence)? BestSupportedPair(
        SvgNumberRecognition top,
        SvgNumberRecognition bottom)
    {
        var topCandidates = CandidateList(top);
        var bottomCandidates = CandidateList(bottom);

        return topCandidates
            .SelectMany(t => bottomCandidates.Select(b => new
            {
                Beats = t.Value,
                Value = b.Value,
                TopConfidence = t.Confidence,
                BottomConfidence = b.Confidence,
                Score = Math.Sqrt(t.Confidence * b.Confidence)
            }))
            .Where(x => SupportedMeters.Contains((x.Beats, x.Value)))
            .Where(x => x.TopConfidence >= 0.005 && x.BottomConfidence >= 0.005)
            .OrderByDescending(x => x.Score)
            .Select(x => ((int Beats, int Value, double TopConfidence, double BottomConfidence)?)
                (x.Beats, x.Value, x.TopConfidence, x.BottomConfidence))
            .FirstOrDefault();
    }

    private static IReadOnlyList<SvgNumberCandidate> CandidateList(SvgNumberRecognition result)
    {
        if (result.Candidates.Count > 0)
            return result.Candidates.Take(8).ToArray();

        return result.Value is not null
            ? new[] { new SvgNumberCandidate(result.Value.Value, result.Confidence) }
            : Array.Empty<SvgNumberCandidate>();
    }

    private static double HorizontalOverlapRatio(RectD a, RectD b)
    {
        var overlap = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left));
        return overlap / Math.Max(1e-9, Math.Min(a.Width, b.Width));
    }

    private static double VerticalOverlapRatio(RectD a, RectD b)
    {
        var overlap = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
        return overlap / Math.Max(1e-9, Math.Min(a.Height, b.Height));
    }

    private static double Ratio(double a, double b) =>
        Math.Min(a, b) / Math.Max(1e-9, Math.Max(a, b));

    private static double Area(RectD rect) => rect.Width * rect.Height;

    private static bool HasPositiveAreaOverlap(RectD a, RectD b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return width > 1e-6 && height > 1e-6;
    }

    private static RectD Union(RectD a, RectD b) =>
        new(
            Math.Min(a.Left, b.Left),
            Math.Min(a.Top, b.Top),
            Math.Max(a.Right, b.Right),
            Math.Max(a.Bottom, b.Bottom));

    private sealed record RowCluster(
        IReadOnlyList<MusicSymbolCandidate> Symbols,
        RectD Bounds);

    private sealed record MeterCandidate(
        MeterSide Side,
        RowCluster Top,
        RowCluster Bottom,
        RectD Bounds,
        double GeometryScore);

    private sealed record ScoredMeter(MeterResolution Meter, double Score);
}
