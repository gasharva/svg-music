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
    private readonly double _minimumConfidence;

    public ClefResolver(IClefRecognizer recognizer, double minimumConfidence = 0.16)
    {
        _recognizer = recognizer;
        _minimumConfidence = minimumConfidence;
    }

    public IReadOnlyList<ClefResolution> Resolve(
        PartMeasureBlock block,
        PrimitiveResolution primitives,
        LogicalGridResolution grid)
    {
        if (!grid.TryGetBlock(block.PartNumber, block.MeasureNumber, out var logicalBlock))
            return Array.Empty<ClefResolution>();

        var h = Math.Max(1e-9, block.PhysicalBounds.Height);
        var available = primitives.Primitives
            .Where(x =>
                x.Scope == PrimitiveLogicalScope.PartMeasure &&
                x.PartNumber == block.PartNumber &&
                x.MeasureNumber == block.MeasureNumber)
            .Where(x => x.PhysicalBounds.Height >= h * 0.52)
            .Where(x => x.PhysicalBounds.Height <= h * 3.10)
            .Where(x => x.PhysicalBounds.Width >= h * 0.10)
            .Where(x => x.PhysicalBounds.Width <= h * 1.80)
            .OrderBy(x => x.PhysicalBounds.Left)
            .ToArray();

        if (available.Length == 0)
            return Array.Empty<ClefResolution>();

        var recognized = new List<ScoredClef>();
        foreach (var candidate in BuildCandidates(available, h))
        {
            var recognition = _recognizer.Recognize(ToContours(candidate.Primitives));
            if (recognition.Symbol is null || recognition.Confidence < _minimumConfidence)
                continue;

            var kind = recognition.Symbol.Value switch
            {
                ClefSymbol.G => ClefKind.G,
                ClefSymbol.F => ClefKind.F,
                ClefSymbol.C => ClefKind.C,
                _ => throw new ArgumentOutOfRangeException()
            };

            var logicalBounds = logicalBlock.ToLogical(candidate.Bounds);
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

        // The same physical clef may be represented by an anchor alone and by an anchor+dots cluster.
        // Keep the strongest overlapping interpretation.
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

    private static IReadOnlyList<Candidate> BuildCandidates(
        IReadOnlyList<ResolvedPrimitive> primitives,
        double staffHeight)
    {
        var result = new List<Candidate>();

        foreach (var anchor in primitives)
        {
            result.Add(new Candidate(new[] { anchor }, anchor.PhysicalBounds));

            var neighbors = primitives
                .Where(x => x.Id != anchor.Id)
                .Where(x => Math.Abs(x.PhysicalBounds.CenterX - anchor.PhysicalBounds.CenterX) <= staffHeight * 0.62)
                .Where(x => x.PhysicalBounds.Top <= anchor.PhysicalBounds.Bottom + staffHeight * 0.42)
                .Where(x => x.PhysicalBounds.Bottom >= anchor.PhysicalBounds.Top - staffHeight * 0.42)
                .OrderBy(x => Distance(anchor.PhysicalBounds, x.PhysicalBounds))
                .Take(3)
                .ToArray();

            if (neighbors.Length == 0)
                continue;

            var group = new List<ResolvedPrimitive> { anchor };
            foreach (var neighbor in neighbors)
            {
                var proposed = Union(group.Select(x => x.PhysicalBounds).Append(neighbor.PhysicalBounds));
                if (proposed.Width > staffHeight * 1.85 || proposed.Height > staffHeight * 3.15)
                    continue;
                group.Add(neighbor);
            }

            if (group.Count > 1)
                result.Add(new Candidate(group.ToArray(), Union(group.Select(x => x.PhysicalBounds))));
        }

        return result
            .GroupBy(x => string.Join(',', x.Primitives.Select(p => p.Id).OrderBy(id => id)))
            .Select(x => x.First())
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> ToContours(
        IEnumerable<ResolvedPrimitive> primitives) =>
        primitives
            .Where(x => x.Contour.Points.Count >= 3)
            .Select(x => (IReadOnlyList<Vector2>)x.Contour.Points)
            .ToArray();

    private static double Distance(RectD a, RectD b)
    {
        var dx = a.CenterX - b.CenterX;
        var dy = a.CenterY - b.CenterY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static RectD Union(IEnumerable<RectD> rects)
    {
        var array = rects.ToArray();
        return new RectD(
            array.Min(x => x.Left),
            array.Min(x => x.Top),
            array.Max(x => x.Right),
            array.Max(x => x.Bottom));
    }

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
