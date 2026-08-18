using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds beam strips geometrically. Level 1 beams terminate at free stem ends on both sides.
/// Higher levels may terminate at only one stem and may touch stems anywhere along their height,
/// but every touched stem must already be covered by the immediately preceding beam level.
/// </summary>
public sealed class BeamResolver
{
    public double MinWidthInStaffSpaces { get; init; } = 0.75;
    public double MinThicknessInStaffSpaces { get; init; } = 0.12;
    public double MaxThicknessInStaffSpaces { get; init; } = 0.70;
    public double EndpointBandFraction { get; init; } = 0.14;
    public double StemXTouchToleranceInStaffSpaces { get; init; } = 0.28;
    public double StemYTouchToleranceInStaffSpaces { get; init; } = 0.38;

    public IReadOnlyList<BeamResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<StemResolution> stems)
    {
        var candidates = BuildCandidates(primitives, grid, stems);
        var result = new List<BeamResolution>();
        var consumedPrimitiveIds = new HashSet<int>();

        ResolveLevelOne(candidates, stems, result, consumedPrimitiveIds);
        ResolveHigherLevels(candidates, stems, result, consumedPrimitiveIds);

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ThenBy(x => x.Level)
            .ThenBy(x => x.PhysicalBounds.Top)
            .ToArray();
    }

    private IReadOnlyList<BeamCandidate> BuildCandidates(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<StemResolution> stems)
    {
        var result = new List<BeamCandidate>();

        foreach (var primitive in primitives.Primitives)
        {
            if (primitive.MeasureNumber is null)
                continue;

            if (primitive.Scope is not (PrimitiveLogicalScope.PartMeasure or PrimitiveLogicalScope.Measure))
                continue;

            var measureNumber = primitive.MeasureNumber.Value;
            var measureStems = stems
                .Where(x => x.MeasureNumber == measureNumber)
                .ToArray();
            if (measureStems.Length == 0)
                continue;

            var staffSpace = StaffSpaceFor(primitive, grid);
            if (staffSpace <= 1e-9)
                continue;

            var strip = TryBuildStrip(primitive.Contour, primitive.PhysicalBounds, staffSpace);
            if (strip is null)
                continue;

            result.Add(new BeamCandidate(
                primitive.Id,
                measureNumber,
                primitive.PhysicalBounds,
                strip.LeftEndpoint,
                strip.RightEndpoint,
                staffSpace));
        }

        return result;
    }

    private void ResolveLevelOne(
        IReadOnlyList<BeamCandidate> candidates,
        IReadOnlyList<StemResolution> stems,
        ICollection<BeamResolution> result,
        ISet<int> consumedPrimitiveIds)
    {
        foreach (var candidate in candidates)
        {
            var measureStems = stems
                .Where(x => x.MeasureNumber == candidate.MeasureNumber)
                .ToArray();

            var xTolerance = candidate.StaffSpace * StemXTouchToleranceInStaffSpaces;
            var yTolerance = candidate.StaffSpace * StemYTouchToleranceInStaffSpaces;

            var leftStem = FindStemAtFreeEndpoint(candidate.LeftEndpoint, measureStems, xTolerance, yTolerance);
            if (leftStem is null)
                continue;

            var rightStem = FindStemAtFreeEndpoint(
                candidate.RightEndpoint,
                measureStems,
                xTolerance,
                yTolerance,
                leftStem);
            if (rightStem is null)
                continue;

            var touchedStems = FindAllTouchedStems(candidate, measureStems, xTolerance, yTolerance);
            if (!touchedStems.Contains(leftStem))
                touchedStems.Add(leftStem);
            if (!touchedStems.Contains(rightStem))
                touchedStems.Add(rightStem);

            result.Add(new BeamResolution(
                candidate.MeasureNumber,
                candidate.PhysicalBounds,
                candidate.LeftEndpoint,
                candidate.RightEndpoint,
                1,
                OrderStems(touchedStems),
                leftStem,
                rightStem));

            consumedPrimitiveIds.Add(candidate.PrimitiveId);
        }
    }

    private void ResolveHigherLevels(
        IReadOnlyList<BeamCandidate> candidates,
        IReadOnlyList<StemResolution> stems,
        ICollection<BeamResolution> result,
        ISet<int> consumedPrimitiveIds)
    {
        var level = 2;

        while (true)
        {
            var previousLevel = result
                .Where(x => x.Level == level - 1)
                .ToArray();
            if (previousLevel.Length == 0)
                break;

            var addedThisLevel = 0;

            foreach (var candidate in candidates.Where(x => !consumedPrimitiveIds.Contains(x.PrimitiveId)))
            {
                var previousInMeasure = previousLevel
                    .Where(x => x.MeasureNumber == candidate.MeasureNumber)
                    .ToArray();
                if (previousInMeasure.Length == 0)
                    continue;

                var coveredByPreviousLevel = previousInMeasure
                    .SelectMany(x => x.Stems)
                    .Distinct()
                    .ToHashSet();

                var measureStems = stems
                    .Where(x => x.MeasureNumber == candidate.MeasureNumber)
                    .ToArray();

                var xTolerance = candidate.StaffSpace * StemXTouchToleranceInStaffSpaces;
                var yTolerance = candidate.StaffSpace * StemYTouchToleranceInStaffSpaces;
                var touchedStems = FindAllTouchedStems(candidate, measureStems, xTolerance, yTolerance);

                if (touchedStems.Count == 0)
                    continue;

                // A secondary/tertiary beam belongs to level N only when all stems it touches are
                // already represented by level N-1 beams.
                if (touchedStems.Any(x => !coveredByPreviousLevel.Contains(x)))
                    continue;

                var leftStem = FindStemAtAnyPoint(candidate.LeftEndpoint, measureStems, xTolerance, yTolerance);
                var rightStem = FindStemAtAnyPoint(
                    candidate.RightEndpoint,
                    measureStems,
                    xTolerance,
                    yTolerance,
                    leftStem);

                // Unlike level 1, only one end is required to terminate at a stem.
                if (leftStem is null && rightStem is null)
                    continue;

                result.Add(new BeamResolution(
                    candidate.MeasureNumber,
                    candidate.PhysicalBounds,
                    candidate.LeftEndpoint,
                    candidate.RightEndpoint,
                    level,
                    OrderStems(touchedStems),
                    leftStem,
                    rightStem));

                consumedPrimitiveIds.Add(candidate.PrimitiveId);
                addedThisLevel++;
            }

            if (addedThisLevel == 0)
                break;

            level++;
        }
    }

    private StripCandidate? TryBuildStrip(
        PrimitiveContour contour,
        RectD bounds,
        double staffSpace)
    {
        if (contour.Points.Count < 4 || bounds.Width <= 1e-9 || bounds.Height <= 1e-9)
            return null;

        if (bounds.Width / staffSpace < MinWidthInStaffSpaces)
            return null;

        // A slanted beam can have a tall overall bbox, so measure thickness locally at both ends
        // instead of using bbox.Height.
        var bandWidth = Math.Max(bounds.Width * EndpointBandFraction, staffSpace * 0.08);
        var leftPoints = contour.Points
            .Where(p => p.X <= bounds.Left + bandWidth)
            .ToArray();
        var rightPoints = contour.Points
            .Where(p => p.X >= bounds.Right - bandWidth)
            .ToArray();

        if (leftPoints.Length < 2 || rightPoints.Length < 2)
            return null;

        var leftMinY = leftPoints.Min(p => (double)p.Y);
        var leftMaxY = leftPoints.Max(p => (double)p.Y);
        var rightMinY = rightPoints.Min(p => (double)p.Y);
        var rightMaxY = rightPoints.Max(p => (double)p.Y);

        var leftThickness = leftMaxY - leftMinY;
        var rightThickness = rightMaxY - rightMinY;
        var averageThickness = (leftThickness + rightThickness) / 2.0;
        var thicknessInStaffSpaces = averageThickness / staffSpace;

        if (thicknessInStaffSpaces < MinThicknessInStaffSpaces ||
            thicknessInStaffSpaces > MaxThicknessInStaffSpaces)
            return null;

        var minThickness = Math.Max(1e-9, Math.Min(leftThickness, rightThickness));
        var maxThickness = Math.Max(leftThickness, rightThickness);
        if (maxThickness / minThickness > 2.2)
            return null;

        var leftEndpoint = new PointD(bounds.Left, (leftMinY + leftMaxY) / 2.0);
        var rightEndpoint = new PointD(bounds.Right, (rightMinY + rightMaxY) / 2.0);
        return new StripCandidate(leftEndpoint, rightEndpoint);
    }

    private static List<StemResolution> FindAllTouchedStems(
        BeamCandidate beam,
        IReadOnlyList<StemResolution> stems,
        double xTolerance,
        double yTolerance)
    {
        var result = new List<StemResolution>();

        foreach (var stem in stems)
        {
            var stemX = stem.PhysicalBounds.CenterX;
            if (stemX < beam.LeftEndpoint.X - xTolerance ||
                stemX > beam.RightEndpoint.X + xTolerance)
                continue;

            var beamY = InterpolateBeamY(beam.LeftEndpoint, beam.RightEndpoint, stemX);
            if (beamY < stem.PhysicalBounds.Top - yTolerance ||
                beamY > stem.PhysicalBounds.Bottom + yTolerance)
                continue;

            result.Add(stem);
        }

        return result;
    }

    private static StemResolution? FindStemAtFreeEndpoint(
        PointD endpoint,
        IReadOnlyList<StemResolution> stems,
        double xTolerance,
        double yTolerance,
        StemResolution? exclude = null)
    {
        return stems
            .Where(x => !ReferenceEquals(x, exclude))
            .Select(x => new { Stem = x, Point = FreeEndpoint(x) })
            .Where(x => Math.Abs(x.Point.X - endpoint.X) <= xTolerance)
            .Where(x => Math.Abs(x.Point.Y - endpoint.Y) <= yTolerance)
            .OrderBy(x => Distance(endpoint, x.Point))
            .Select(x => x.Stem)
            .FirstOrDefault();
    }

    private static StemResolution? FindStemAtAnyPoint(
        PointD endpoint,
        IReadOnlyList<StemResolution> stems,
        double xTolerance,
        double yTolerance,
        StemResolution? exclude = null)
    {
        return stems
            .Where(x => !ReferenceEquals(x, exclude))
            .Where(x => Math.Abs(x.PhysicalBounds.CenterX - endpoint.X) <= xTolerance)
            .Where(x => endpoint.Y >= x.PhysicalBounds.Top - yTolerance &&
                        endpoint.Y <= x.PhysicalBounds.Bottom + yTolerance)
            .OrderBy(x => DistanceToVerticalStem(endpoint, x.PhysicalBounds))
            .FirstOrDefault();
    }

    private static IReadOnlyList<StemResolution> OrderStems(IEnumerable<StemResolution> stems) =>
        stems
            .Distinct()
            .OrderBy(x => x.PhysicalBounds.CenterX)
            .ThenBy(x => x.PhysicalBounds.Top)
            .ToArray();

    private static PointD FreeEndpoint(StemResolution stem) =>
        stem.Direction == StemDirection.Up
            ? new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Top)
            : new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Bottom);

    private static double InterpolateBeamY(PointD left, PointD right, double x)
    {
        var dx = right.X - left.X;
        if (Math.Abs(dx) <= 1e-9)
            return (left.Y + right.Y) / 2.0;

        var t = (x - left.X) / dx;
        return left.Y + t * (right.Y - left.Y);
    }

    private static double StaffSpaceFor(ResolvedPrimitive primitive, LogicalGridResolution grid)
    {
        if (primitive.PartNumber is { } part &&
            primitive.MeasureNumber is { } measure &&
            grid.TryGetBlock(part, measure, out var ownBlock))
            return ownBlock.PhysicalBounds.Height / 4.0;

        if (primitive.MeasureNumber is not { } measureNumber)
            return 0;

        var blocks = grid.Blocks
            .Where(x => x.MeasureNumber == measureNumber)
            .ToArray();
        return blocks.Length == 0
            ? 0
            : blocks.Average(x => x.PhysicalBounds.Height / 4.0);
    }

    private static double Distance(PointD a, PointD b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistanceToVerticalStem(PointD point, RectD stem)
    {
        var dx = Math.Abs(point.X - stem.CenterX);
        var dy = point.Y < stem.Top
            ? stem.Top - point.Y
            : point.Y > stem.Bottom
                ? point.Y - stem.Bottom
                : 0.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed record StripCandidate(PointD LeftEndpoint, PointD RightEndpoint);

    private sealed record BeamCandidate(
        int PrimitiveId,
        int MeasureNumber,
        RectD PhysicalBounds,
        PointD LeftEndpoint,
        PointD RightEndpoint,
        double StaffSpace);
}
