using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Detects short horizontal ledger-line primitives and combines them into continuous ladders.
/// No glyph recognition is involved: this is pure logical/physical geometry.
/// </summary>
public sealed class LedgerLineResolver
{
    public double MaxCenterOffsetInLogicalY { get; init; } = 0.35;
    public double MinWidthInStaffSpaces { get; init; } = 0.50;
    public double MaxWidthInStaffSpaces { get; init; } = 3.00;
    public double MaxThicknessInStaffSpaces { get; init; } = 0.25;
    public double MinHorizontalOverlapRatio { get; init; } = 0.35;

    public IReadOnlyList<LedgerLineResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid)
    {
        var partMeasurePrimitives = primitives.Primitives
            .Where(x => x.Scope == PrimitiveLogicalScope.PartMeasure)
            .Where(x => x.PartNumber is not null && x.MeasureNumber is not null)
            .ToArray();

        var candidates = new List<Candidate>();
        foreach (var primitive in partMeasurePrimitives)
        {
            var partNumber = primitive.PartNumber!.Value;
            var measureNumber = primitive.MeasureNumber!.Value;
            if (!grid.TryGetBlock(partNumber, measureNumber, out var block))
                continue;

            var candidate = TryBuildCandidate(primitive, block);
            if (candidate is not null)
                candidates.Add(candidate);
        }

        var groupedByBlockAndSide = candidates
            .GroupBy(x => new { x.PartNumber, x.MeasureNumber, x.Side })
            .ToArray();

        var result = new List<LedgerLineResolution>();
        foreach (var blockGroup in groupedByBlockAndSide)
        {
            foreach (var cluster in BuildHorizontalClusters(blockGroup.ToArray()))
            {
                var ladder = BuildContinuousLadder(cluster);
                if (ladder is not null)
                    result.Add(ladder);
            }
        }

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.LogicalBounds.Left ?? double.MinValue)
            .ThenBy(x => x.Depth)
            .ToArray();
    }

    private Candidate? TryBuildCandidate(
        ResolvedPrimitive primitive,
        LogicalGridBlock block)
    {
        var physical = primitive.PhysicalBounds;
        var staffSpace = block.PhysicalBounds.Height / 4.0;
        if (staffSpace <= 1e-9)
            return null;

        var widthInStaffSpaces = physical.Width / staffSpace;
        if (widthInStaffSpaces < MinWidthInStaffSpaces ||
            widthInStaffSpaces > MaxWidthInStaffSpaces)
            return null;

        var thicknessInStaffSpaces = physical.Height / staffSpace;
        if (thicknessInStaffSpaces > MaxThicknessInStaffSpaces)
            return null;

        var logical = block.ToLogical(physical);
        var centerY = (logical.Top + logical.Bottom) / 2.0;
        var nearestLineLevel = (int)Math.Round(centerY / 2.0) * 2;
        var centerOffset = Math.Abs(centerY - nearestLineLevel);
        if (centerOffset > MaxCenterOffsetInLogicalY)
            return null;

        var side = nearestLineLevel <= -2
            ? LedgerSide.Above
            : nearestLineLevel >= 10
                ? LedgerSide.Below
                : LedgerSide.None;

        if (side == LedgerSide.None)
            return null;

        return new Candidate(
            primitive.PartNumber!.Value,
            primitive.MeasureNumber!.Value,
            side,
            nearestLineLevel,
            physical,
            logical);
    }

    private IReadOnlyList<IReadOnlyList<Candidate>> BuildHorizontalClusters(
        IReadOnlyList<Candidate> candidates)
    {
        var remaining = new HashSet<int>(Enumerable.Range(0, candidates.Count));
        var result = new List<IReadOnlyList<Candidate>>();

        while (remaining.Count > 0)
        {
            var seedIndex = remaining.First();
            remaining.Remove(seedIndex);

            var clusterIndices = new HashSet<int> { seedIndex };
            var queue = new Queue<int>();
            queue.Enqueue(seedIndex);

            while (queue.Count > 0)
            {
                var currentIndex = queue.Dequeue();
                var current = candidates[currentIndex];

                var neighbors = remaining
                    .Where(index => HorizontallyBelongsTogether(current, candidates[index]))
                    .ToArray();

                foreach (var neighborIndex in neighbors)
                {
                    remaining.Remove(neighborIndex);
                    clusterIndices.Add(neighborIndex);
                    queue.Enqueue(neighborIndex);
                }
            }

            result.Add(clusterIndices.Select(index => candidates[index]).ToArray());
        }

        return result;
    }

    private bool HorizontallyBelongsTogether(Candidate a, Candidate b)
    {
        var intersectionLeft = Math.Max(a.PhysicalBounds.Left, b.PhysicalBounds.Left);
        var intersectionRight = Math.Min(a.PhysicalBounds.Right, b.PhysicalBounds.Right);
        var intersectionWidth = Math.Max(0, intersectionRight - intersectionLeft);
        var smallerWidth = Math.Min(a.PhysicalBounds.Width, b.PhysicalBounds.Width);
        var overlapRatio = intersectionWidth / Math.Max(1e-9, smallerWidth);

        return overlapRatio >= MinHorizontalOverlapRatio;
    }

    private static LedgerLineResolution? BuildContinuousLadder(IReadOnlyList<Candidate> cluster)
    {
        if (cluster.Count == 0)
            return null;

        var side = cluster[0].Side;
        var byLevel = cluster
            .GroupBy(x => x.Level)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var firstLevel = side == LedgerSide.Above ? -2 : 10;
        var step = side == LedgerSide.Above ? -2 : 2;

        var accepted = new List<Candidate>();
        var level = firstLevel;
        while (byLevel.TryGetValue(level, out var atLevel))
        {
            accepted.AddRange(atLevel);
            level += step;
        }

        if (accepted.Count == 0)
            return null;

        var depth = accepted
            .Select(x => x.Level)
            .Distinct()
            .Count();
        if (side == LedgerSide.Above)
            depth = -depth;

        var logicalBounds = UnionLogicalBounds(accepted.Select(x => x.LogicalBounds));
        var first = accepted[0];

        return new LedgerLineResolution(
            first.PartNumber,
            first.MeasureNumber,
            logicalBounds,
            depth);
    }

    private static LogicalRectD UnionLogicalBounds(IEnumerable<LogicalRectD> bounds)
    {
        var items = bounds.ToArray();
        var leftValues = items.Where(x => x.Left is not null).Select(x => x.Left!.Value).ToArray();
        var rightValues = items.Where(x => x.Right is not null).Select(x => x.Right!.Value).ToArray();

        return new LogicalRectD(
            leftValues.Length == items.Length ? leftValues.Min() : null,
            items.Min(x => x.Top),
            rightValues.Length == items.Length ? rightValues.Max() : null,
            items.Max(x => x.Bottom));
    }

    private enum LedgerSide
    {
        None,
        Above,
        Below
    }

    private sealed record Candidate(
        int PartNumber,
        int MeasureNumber,
        LedgerSide Side,
        int Level,
        RectD PhysicalBounds,
        LogicalRectD LogicalBounds);
}
