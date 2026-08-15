using System.Numerics;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Pipeline step 4. Finds clefs from already-resolved primitives.
/// Position inside the measure is deliberately not used as a prior: clef changes may occur anywhere.
/// </summary>
public sealed class ClefResolver
{
    private readonly IClefRecognizer _recognizer;
    private readonly ClefCandidateSanity _sanity;
    private readonly double _minimumConfidence;

    public ClefResolver(
        IClefRecognizer recognizer,
        ClefCandidateSanity? sanity = null,
        double minimumConfidence = 0.0)
    {
        _recognizer = recognizer;
        _sanity = sanity ?? new ClefCandidateSanity();
        _minimumConfidence = minimumConfidence;
    }

    public IReadOnlyList<ClefResolution> Resolve(
        PartMeasureBlock block,
        PrimitiveResolution primitives,
        LogicalGridResolution grid)
    {
        if (!grid.TryGetBlock(block.PartNumber, block.MeasureNumber, out var logicalBlock))
            return Array.Empty<ClefResolution>();

        var staffHeight = Math.Max(1e-9, block.PhysicalBounds.Height);
        var available = primitives.Primitives
            .Where(x =>
                x.Scope == PrimitiveLogicalScope.PartMeasure &&
                x.PartNumber == block.PartNumber &&
                x.MeasureNumber == block.MeasureNumber)
            .OrderBy(x => x.PhysicalBounds.Left)
            .ToArray();

        if (available.Length == 0)
            return Array.Empty<ClefResolution>();

        var recognized = new List<ScoredClef>();
        foreach (var candidate in BuildCandidates(available, logicalBlock, staffHeight))
        {
            var logicalBounds = logicalBlock.ToLogical(candidate.Bounds);
            if (!_sanity.Accept(logicalBounds, candidate.Bounds, staffHeight))
                continue;

            var merged = MergeInsideBounds(candidate, available);

            if (_recognizer is DiagnosticClefRecognizer diagnostic)
            {
                diagnostic.SetNextContext(new ClefDiagnosticContext(
                    block.PartNumber,
                    block.MeasureNumber,
                    logicalBounds));
            }

            var recognition = _recognizer.Recognize(ToContours(merged));
            if (recognition.Symbol is null || recognition.Confidence < _minimumConfidence)
                continue;

            var kind = recognition.Symbol.Value switch
            {
                ClefSymbol.G => ClefKind.G,
                ClefSymbol.F => ClefKind.F,
                ClefSymbol.C => ClefKind.C,
                _ => throw new ArgumentOutOfRangeException()
            };

            recognized.Add(new ScoredClef(
                new ClefResolution(
                    block.PartNumber,
                    block.MeasureNumber,
                    kind,
                    recognition.Confidence,
                    candidate.Bounds,
                    logicalBounds),
                recognition.Confidence));
        }

        var result = new List<ClefResolution>();
        foreach (var item in recognized.OrderByDescending(x => x.Score))
        {
            if (result.Any(existing =>
                    existing.Kind == item.Clef.Kind &&
                    OverlapRatio(existing.PhysicalBounds, item.Clef.PhysicalBounds) >= 0.45))
                continue;

            result.Add(item.Clef);
        }

        return result.OrderBy(x => x.PhysicalBounds.Left).ToArray();
    }

    private IReadOnlyList<Candidate> BuildCandidates(
        IReadOnlyList<ResolvedPrimitive> primitives,
        LogicalGridBlock logicalBlock,
        double staffHeight)
    {
        var result = new List<Candidate>();

        foreach (var anchor in primitives)
        {
            var logical = logicalBlock.ToLogical(anchor.PhysicalBounds);
            if (!_sanity.Accept(logical, anchor.PhysicalBounds, staffHeight))
                continue;

            result.Add(new Candidate(new[] { anchor }, anchor.PhysicalBounds));
        }

        return result
            .GroupBy(x => (
                X: Math.Round(x.Bounds.CenterX, 2),
                Y: Math.Round(x.Bounds.CenterY, 2),
                W: Math.Round(x.Bounds.Width, 2),
                H: Math.Round(x.Bounds.Height, 2)))
            .Select(x => x.First())
            .ToArray();
    }

    private static IReadOnlyList<ResolvedPrimitive> MergeInsideBounds(
        Candidate candidate,
        IReadOnlyList<ResolvedPrimitive> available)
    {
        var b = candidate.Bounds;
        var padX = Math.Max(0.5, b.Width * 0.12);
        var padY = Math.Max(0.5, b.Height * 0.08);
        var expanded = new RectD(
            b.Left - padX,
            b.Top - padY,
            b.Right + padX,
            b.Bottom + padY);

        return available
            .Where(x => Contains(expanded, x.PhysicalBounds.CenterX, x.PhysicalBounds.CenterY))
            .OrderBy(x => x.Id)
            .ToArray();
    }

    private static bool Contains(RectD b, double x, double y) =>
        x >= b.Left && x <= b.Right && y >= b.Top && y <= b.Bottom;

    private static IReadOnlyList<IReadOnlyList<Vector2>> ToContours(
        IEnumerable<ResolvedPrimitive> primitives) =>
        primitives
            .Where(x => x.Contour.Points.Count >= 3)
            .Select(x => (IReadOnlyList<Vector2>)x.Contour.Points)
            .ToArray();

    private static double OverlapRatio(RectD a, RectD b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top)
            return 0d;

        var intersection = (right - left) * (bottom - top);
        var smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return intersection / Math.Max(1e-9, smaller);
    }

    private sealed record Candidate(
        IReadOnlyList<ResolvedPrimitive> Primitives,
        RectD Bounds);

    private sealed record ScoredClef(ClefResolution Clef, double Score);
}
