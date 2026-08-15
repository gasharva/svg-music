using System.Numerics;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Pipeline step 3. Works exclusively on step-2 primitives. It first finds a very small set of
/// geometrically plausible meter clusters, then invokes the heavier number recognizer only for
/// those clusters. The source SVG is deliberately not accessible here.
/// </summary>
public sealed class MeterResolver
{
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

    private static readonly double[] WindowWidthsInStaffHeights = { 0.55, 0.75, 0.95 };
    private readonly ISvgNumberRecognizer _numberRecognizer;

    public MeterResolver(ISvgNumberRecognizer numberRecognizer) =>
        _numberRecognizer = numberRecognizer;

    public MeterResolution? Resolve(PartMeasureBlock block, PrimitiveResolution primitives)
    {
        var available = primitives.Primitives
            .Where(x =>
                x.Scope == PrimitiveLogicalScope.PartMeasure &&
                x.PartNumber == block.PartNumber &&
                x.MeasureNumber == block.MeasureNumber ||
                x.Scope == PrimitiveLogicalScope.Measure &&
                x.MeasureNumber == block.MeasureNumber)
            .Where(x => x.PhysicalBounds.IntersectsHorizontally(
                block.PhysicalBounds.Left,
                block.PhysicalBounds.Right))
            .ToArray();

        if (available.Length < 2 || block.PhysicalBounds.Height <= 0)
            return null;

        var windows = BuildWindows(block, available, MeterSide.Left)
            .Concat(BuildWindows(block, available, MeterSide.Right))
            .OrderByDescending(x => x.GeometryScore)
            .Take(4)
            .ToArray();

        var recognized = new List<ScoredMeter>();
        foreach (var window in windows)
        {
            var meter = RecognizeWindow(block, window);
            if (meter is not null)
                recognized.Add(meter);
        }

        return recognized
            .OrderByDescending(x => x.Score)
            .Select(x => x.Meter)
            .FirstOrDefault();
    }

    private static IReadOnlyList<CandidateWindow> BuildWindows(
        PartMeasureBlock block,
        IReadOnlyList<ResolvedPrimitive> primitives,
        MeterSide side)
    {
        var b = block.PhysicalBounds;
        var height = b.Height;
        var leftLimit = b.Left + b.Width * 0.48;
        var rightLimit = b.Right - b.Width * 0.36;

        var sidePrimitives = primitives
            .Where(x => side == MeterSide.Left
                ? x.PhysicalBounds.CenterX <= leftLimit
                : x.PhysicalBounds.CenterX >= rightLimit)
            .ToArray();

        var candidates = new List<CandidateWindow>();
        foreach (var anchor in sidePrimitives)
        {
            foreach (var factor in WindowWidthsInStaffHeights)
            {
                var width = Math.Min(b.Width * 0.34, height * factor);
                var left = anchor.PhysicalBounds.CenterX - width / 2;
                var right = anchor.PhysicalBounds.CenterX + width / 2;

                if (side == MeterSide.Left)
                {
                    left = Math.Max(left, b.Left);
                    right = Math.Min(right, leftLimit);
                }
                else
                {
                    left = Math.Max(left, rightLimit);
                    right = Math.Min(right, b.Right);
                }

                if (right <= left)
                    continue;

                var members = sidePrimitives
                    .Where(x => x.PhysicalBounds.CenterX >= left && x.PhysicalBounds.CenterX <= right)
                    .ToArray();
                if (members.Length < 2)
                    continue;

                var minY = members.Min(x => x.PhysicalBounds.Top);
                var maxY = members.Max(x => x.PhysicalBounds.Bottom);
                var coverage = (maxY - minY) / height;
                if (coverage < 0.52)
                    continue;

                var middle = b.CenterY;
                var upper = members.Count(x => x.PhysicalBounds.CenterY < middle);
                var lower = members.Length - upper;
                if (upper == 0 || lower == 0)
                    continue;

                var balance = 1d - Math.Abs(upper - lower) / (double)members.Length;
                var edge = side == MeterSide.Left
                    ? 1d - Math.Clamp((left - b.Left) / Math.Max(1, b.Width * 0.48), 0, 1)
                    : Math.Clamp((right - rightLimit) / Math.Max(1, b.Right - rightLimit), 0, 1);
                var geometryScore = Math.Min(coverage, 1.2) + 0.25 * balance + 0.12 * edge;

                candidates.Add(new CandidateWindow(
                    side,
                    new RectD(left, minY, right, maxY),
                    members,
                    geometryScore));
            }
        }

        return candidates
            .OrderByDescending(x => x.GeometryScore)
            .GroupBy(x => Math.Round(x.Bounds.CenterX / Math.Max(1, height * 0.20)))
            .Select(x => x.First())
            .Take(2)
            .ToArray();
    }

    private ScoredMeter? RecognizeWindow(PartMeasureBlock block, CandidateWindow window)
    {
        var middleY = block.PhysicalBounds.CenterY;
        var upper = window.Primitives.Where(x => x.PhysicalBounds.CenterY < middleY).ToArray();
        var lower = window.Primitives.Where(x => x.PhysicalBounds.CenterY >= middleY).ToArray();
        if (upper.Length == 0 || lower.Length == 0)
            return null;

        var top = _numberRecognizer.Recognize(ToContours(upper));
        var bottom = _numberRecognizer.Recognize(ToContours(lower));

        if (top.Value is null || bottom.Value is null ||
            top.Confidence < 0.04 || bottom.Confidence < 0.04 ||
            !SupportedMeters.Contains((top.Value.Value, bottom.Value.Value)))
            return null;

        var confidence = Math.Sqrt(top.Confidence * bottom.Confidence);
        var numeratorBounds = BoundsOf(upper);
        var denominatorBounds = BoundsOf(lower);
        var totalBounds = Union(numeratorBounds, denominatorBounds);

        return new ScoredMeter(
            new MeterResolution(
                block.PartNumber,
                block.MeasureNumber,
                top.Value.Value,
                bottom.Value.Value,
                window.Side,
                confidence,
                totalBounds,
                numeratorBounds,
                denominatorBounds),
            confidence + 0.18 * window.GeometryScore);
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> ToContours(
        IEnumerable<ResolvedPrimitive> primitives) =>
        primitives
            .Where(x => x.Contour.Points.Count >= 3)
            .Select(x => (IReadOnlyList<Vector2>)x.Contour.Points)
            .ToArray();

    private static RectD BoundsOf(IReadOnlyList<ResolvedPrimitive> primitives) =>
        new(
            primitives.Min(x => x.PhysicalBounds.Left),
            primitives.Min(x => x.PhysicalBounds.Top),
            primitives.Max(x => x.PhysicalBounds.Right),
            primitives.Max(x => x.PhysicalBounds.Bottom));

    private static RectD Union(RectD a, RectD b) =>
        new(
            Math.Min(a.Left, b.Left),
            Math.Min(a.Top, b.Top),
            Math.Max(a.Right, b.Right),
            Math.Max(a.Bottom, b.Bottom));

    private sealed record CandidateWindow(
        MeterSide Side,
        RectD Bounds,
        IReadOnlyList<ResolvedPrimitive> Primitives,
        double GeometryScore);

    private sealed record ScoredMeter(MeterResolution Meter, double Score);
}
