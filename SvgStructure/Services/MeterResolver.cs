using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Pipeline step 3. Resolves a conventional time signature inside one P+M block.
/// Search is deliberately constrained to the two notation positions where a meter can occur:
/// near the left edge of the block or at its far right edge.
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

    private static readonly double[] WindowWidthsInStaffHeights = { 0.52, 0.72, 0.95 };

    private readonly ISvgNumberRecognizer _numberRecognizer;

    public MeterResolver(ISvgNumberRecognizer numberRecognizer)
    {
        _numberRecognizer = numberRecognizer;
    }

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

        if (available.Length == 0 || block.PhysicalBounds.Height <= 0)
            return null;

        var candidates = new List<ScoredMeter>();
        FindCandidates(block, available, MeterSide.Left, candidates, primitives.Structure.SvgPath);
        FindCandidates(block, available, MeterSide.Right, candidates, primitives.Structure.SvgPath);

        return candidates
            .OrderByDescending(x => x.Score)
            .Select(x => x.Meter)
            .FirstOrDefault();
    }

    private void FindCandidates(
        PartMeasureBlock block,
        IReadOnlyList<ResolvedPrimitive> primitives,
        MeterSide side,
        ICollection<ScoredMeter> output,
        string svgPath)
    {
        var b = block.PhysicalBounds;
        var height = b.Height;
        var leftSearchRight = b.Left + b.Width * 0.48;
        var rightSearchLeft = b.Right - b.Width * 0.36;

        var anchors = primitives
            .Select(x => x.PhysicalBounds.CenterX)
            .Where(x => side == MeterSide.Left ? x <= leftSearchRight : x >= rightSearchLeft)
            .DistinctBy(x => Math.Round(x, 1))
            .ToArray();

        foreach (var anchorX in anchors)
        {
            foreach (var widthFactor in WindowWidthsInStaffHeights)
            {
                var windowWidth = Math.Min(b.Width * 0.34, height * widthFactor);
                if (windowWidth <= 0)
                    continue;

                var left = anchorX - windowWidth / 2;
                var right = anchorX + windowWidth / 2;

                if (side == MeterSide.Left)
                {
                    left = Math.Max(left, b.Left);
                    right = Math.Min(right, leftSearchRight);
                }
                else
                {
                    left = Math.Max(left, rightSearchLeft);
                    right = Math.Min(right, b.Right);
                }

                if (right <= left)
                    continue;

                var inWindow = primitives
                    .Where(x => x.PhysicalBounds.CenterX >= left && x.PhysicalBounds.CenterX <= right)
                    .ToArray();
                if (inWindow.Length < 2)
                    continue;

                var minY = inWindow.Min(x => x.PhysicalBounds.Top);
                var maxY = inWindow.Max(x => x.PhysicalBounds.Bottom);
                var verticalCoverage = (maxY - minY) / height;
                if (verticalCoverage < 0.55)
                    continue;

                var middleY = b.CenterY;
                if (!inWindow.Any(x => x.PhysicalBounds.CenterY < middleY) ||
                    !inWindow.Any(x => x.PhysicalBounds.CenterY >= middleY))
                    continue;

                var padX = Math.Max(0.5, (right - left) * 0.04);
                var padY = Math.Max(0.5, height * 0.06);
                var numerator = new RectD(
                    Math.Max(b.Left, left - padX),
                    b.Top - padY,
                    Math.Min(b.Right, right + padX),
                    middleY + height * 0.05);
                var denominator = new RectD(
                    numerator.Left,
                    middleY - height * 0.05,
                    numerator.Right,
                    b.Bottom + padY);

                var top = _numberRecognizer.Recognize(
                    svgPath,
                    numerator.Left,
                    numerator.Top,
                    numerator.Right,
                    numerator.Bottom);
                var bottom = _numberRecognizer.Recognize(
                    svgPath,
                    denominator.Left,
                    denominator.Top,
                    denominator.Right,
                    denominator.Bottom);

                if (top.Value is null || bottom.Value is null ||
                    top.Confidence < 0.04 || bottom.Confidence < 0.04 ||
                    !SupportedMeters.Contains((top.Value.Value, bottom.Value.Value)))
                    continue;

                var confidence = Math.Sqrt(top.Confidence * bottom.Confidence);
                var sidePrior = side == MeterSide.Right
                    ? 0.08 * Math.Clamp((anchorX - rightSearchLeft) / Math.Max(1, b.Right - rightSearchLeft), 0, 1)
                    : 0.02;
                var score = confidence + 0.12 * Math.Min(verticalCoverage, 1.2) + sidePrior;
                var totalBounds = new RectD(
                    Math.Min(numerator.Left, denominator.Left),
                    Math.Min(numerator.Top, denominator.Top),
                    Math.Max(numerator.Right, denominator.Right),
                    Math.Max(numerator.Bottom, denominator.Bottom));

                output.Add(new ScoredMeter(
                    new MeterResolution(
                        block.PartNumber,
                        block.MeasureNumber,
                        top.Value.Value,
                        bottom.Value.Value,
                        side,
                        confidence,
                        totalBounds,
                        numerator,
                        denominator),
                    score));
            }
        }
    }

    private sealed record ScoredMeter(MeterResolution Meter, double Score);
}
