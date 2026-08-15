using System.Numerics;
using Clipper2Lib;
using ShimSkiaSharp;
using Svg.Skia;

namespace SvgSymbols.Services;

public sealed record LegacyIoUClefScore(
    ClefSymbol Symbol,
    double MaskIoU,
    double VectorIoU,
    double Score);

public sealed record LegacyIoUClefAnalysis(
    ClefSymbol? Symbol,
    IReadOnlyList<LegacyIoUClefScore> Candidates);

/// <summary>
/// Diagnostic transplant of the old FastGlyphMatcher idea:
/// bbox-normalized 64x64 binary-mask IoU plus exact polygon IoU through Clipper2.
/// No staff position or size prior is used here; this is deliberately shape-only.
/// </summary>
public sealed class LegacyIoUClefAnalyzer
{
    private const int MaskSize = 64;
    private const double PolygonScale = 10000.0;
    private const int CurveSteps = 16;

    private readonly IReadOnlyList<Reference> _references;

    public LegacyIoUClefAnalyzer(string referenceGlyphDirectory)
    {
        _references = new[]
        {
            BuildReference(ClefSymbol.G, Path.Combine(referenceGlyphDirectory, "gClef.svg")),
            BuildReference(ClefSymbol.F, Path.Combine(referenceGlyphDirectory, "fClef.svg"))
        };
    }

    public LegacyIoUClefAnalysis Analyze(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new LegacyIoUClefAnalysis(null, Array.Empty<LegacyIoUClefScore>());

        var source = Prepare(contours);
        var ranked = _references
            .Select(reference =>
            {
                var maskIoU = BestMaskIoU(source.Mask, reference.Mask);
                var vectorIoU = BestVectorIoU(source.Paths, reference.Paths);
                var score = 0.65 * maskIoU + 0.35 * vectorIoU;
                return new LegacyIoUClefScore(reference.Symbol, maskIoU, vectorIoU, score);
            })
            .OrderByDescending(x => x.Score)
            .ToArray();

        return new LegacyIoUClefAnalysis(ranked.FirstOrDefault()?.Symbol, ranked);
    }

    private static Reference BuildReference(ClefSymbol symbol, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Bravura clef reference not found.", path);

        var contours = ReadSvgContours(path);
        var prepared = Prepare(contours);
        return new Reference(symbol, prepared.Mask, prepared.Paths);
    }

    private static Prepared Prepare(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var normalized = Normalize(contours);
        return new Prepared(CreateMask(normalized), ToPaths(normalized, false, false));
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> Normalize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var all = contours.SelectMany(x => x).ToArray();
        if (all.Length == 0)
            return Array.Empty<IReadOnlyList<Vector2>>();

        var minX = all.Min(p => p.X);
        var maxX = all.Max(p => p.X);
        var minY = all.Min(p => p.Y);
        var maxY = all.Max(p => p.Y);
        var width = Math.Max(maxX - minX, 1e-9f);
        var height = Math.Max(maxY - minY, 1e-9f);

        return contours
            .Where(x => x.Count >= 3)
            .Select(c => (IReadOnlyList<Vector2>)c
                .Select(p => new Vector2((p.X - minX) / width, (p.Y - minY) / height))
                .ToArray())
            .ToArray();
    }

    private static ulong[] CreateMask(IReadOnlyList<IReadOnlyList<Vector2>> normalized)
    {
        var rows = new ulong[MaskSize];
        for (var y = 0; y < MaskSize; y++)
        {
            var py = (y + 0.5) / MaskSize;
            ulong row = 0;
            for (var x = 0; x < MaskSize; x++)
            {
                var px = (x + 0.5) / MaskSize;
                if (InsideEvenOdd(normalized, px, py))
                    row |= 1UL << x;
            }
            rows[y] = row;
        }
        return rows;
    }

    private static double BestMaskIoU(ulong[] source, ulong[] reference)
    {
        var best = MaskIoU(source, reference);
        best = Math.Max(best, MaskIoU(source, Flip(reference, true, false)));
        best = Math.Max(best, MaskIoU(source, Flip(reference, false, true)));
        best = Math.Max(best, MaskIoU(source, Flip(reference, true, true)));
        return best;
    }

    private static double BestVectorIoU(Paths64 source, Paths64 reference)
    {
        var best = VectorIoU(source, reference);
        best = Math.Max(best, VectorIoU(source, FlipPaths(reference, true, false)));
        best = Math.Max(best, VectorIoU(source, FlipPaths(reference, false, true)));
        best = Math.Max(best, VectorIoU(source, FlipPaths(reference, true, true)));
        return best;
    }

    private static double MaskIoU(ulong[] a, ulong[] b)
    {
        long intersection = 0;
        long union = 0;
        for (var i = 0; i < MaskSize; i++)
        {
            intersection += BitOperations.PopCount(a[i] & b[i]);
            union += BitOperations.PopCount(a[i] | b[i]);
        }
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static ulong[] Flip(ulong[] source, bool horizontal, bool vertical)
    {
        var result = new ulong[MaskSize];
        for (var y = 0; y < MaskSize; y++)
        {
            var targetY = vertical ? MaskSize - 1 - y : y;
            var row = source[y];
            if (horizontal)
                row = ReverseBits(row);
            result[targetY] = row;
        }
        return result;
    }

    private static ulong ReverseBits(ulong value)
    {
        value = ((value & 0x5555555555555555UL) << 1) | ((value >> 1) & 0x5555555555555555UL);
        value = ((value & 0x3333333333333333UL) << 2) | ((value >> 2) & 0x3333333333333333UL);
        value = ((value & 0x0F0F0F0F0F0F0F0FUL) << 4) | ((value >> 4) & 0x0F0F0F0F0F0F0F0FUL);
        value = ((value & 0x00FF00FF00FF00FFUL) << 8) | ((value >> 8) & 0x00FF00FF00FF00FFUL);
        value = ((value & 0x0000FFFF0000FFFFUL) << 16) | ((value >> 16) & 0x0000FFFF0000FFFFUL);
        return (value << 32) | (value >> 32);
    }

    private static bool InsideEvenOdd(IReadOnlyList<IReadOnlyList<Vector2>> contours, double x, double y)
    {
        var inside = false;
        foreach (var contour in contours)
        {
            for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
            {
                var pi = contour[i];
                var pj = contour[j];
                if (((pi.Y > y) != (pj.Y > y)) &&
                    x < (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y + 1e-12) + pi.X)
                    inside = !inside;
            }
        }
        return inside;
    }

    private static Paths64 ToPaths(IReadOnlyList<IReadOnlyList<Vector2>> normalized, bool flipX, bool flipY)
    {
        var result = new Paths64();
        foreach (var contour in normalized)
        {
            if (contour.Count < 3)
                continue;

            var path = new Path64(contour.Count);
            foreach (var point in contour)
            {
                var x = flipX ? 1 - point.X : point.X;
                var y = flipY ? 1 - point.Y : point.Y;
                path.Add(new Point64(
                    (long)Math.Round(x * PolygonScale),
                    (long)Math.Round(y * PolygonScale)));
            }
            result.Add(path);
        }
        return result;
    }

    private static Paths64 FlipPaths(Paths64 source, bool flipX, bool flipY)
    {
        var result = new Paths64();
        foreach (var contour in source)
        {
            var path = new Path64(contour.Count);
            foreach (var point in contour)
            {
                var x = point.X / PolygonScale;
                var y = point.Y / PolygonScale;
                if (flipX) x = 1 - x;
                if (flipY) y = 1 - y;
                path.Add(new Point64(
                    (long)Math.Round(x * PolygonScale),
                    (long)Math.Round(y * PolygonScale)));
            }
            result.Add(path);
        }
        return result;
    }

    private static double VectorIoU(Paths64 a, Paths64 b)
    {
        if (a.Count == 0 || b.Count == 0)
            return 0;

        var intersection = Clipper.Intersect(a, b, FillRule.EvenOdd);
        var combined = new Paths64(a);
        combined.AddRange(b);
        var union = Clipper.Union(combined, FillRule.EvenOdd);
        var intersectionArea = Math.Abs(Clipper.Area(intersection));
        var unionArea = Math.Abs(Clipper.Area(union));
        return unionArea <= 0 ? 0 : Math.Clamp(intersectionArea / unionArea, 0.0, 1.0);
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> ReadSvgContours(string path)
    {
        using var svg = SKSvg.CreateFromFile(path);
        var picture = svg.Model
            ?? throw new InvalidOperationException($"Svg.Skia did not produce a retained scene model for '{path}'.");
        var contours = new List<List<Vector2>>();
        ReadPicture(picture, SKMatrix.Identity, contours);
        return contours.Cast<IReadOnlyList<Vector2>>().ToArray();
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
        List<Vector2>? currentContour = null;
        Vector2 current = default;
        var hasCurrent = false;

        void Flush()
        {
            if (currentContour is { Count: >= 3 })
                contours.Add(currentContour);
            currentContour = null;
            hasCurrent = false;
        }

        void Start(Vector2 point)
        {
            Flush();
            currentContour = new List<Vector2> { point };
            current = point;
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
                    Start(Map(matrix, move.X, move.Y));
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
                case ClosePathCommand:
                    Flush();
                    break;
            }
        }
        Flush();
    }

    private static Vector2 Map(SKMatrix matrix, float x, float y)
    {
        var p = matrix.MapPoint(new SKPoint(x, y));
        return new Vector2(p.X, p.Y);
    }

    private sealed record Prepared(ulong[] Mask, Paths64 Paths);
    private sealed record Reference(ClefSymbol Symbol, ulong[] Mask, Paths64 Paths);
}
