using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds primary (level-1) beams geometrically. A beam is a shallow filled strip whose left and
/// right ends both terminate at the free ends of recognized stems. No PCA/glyph recognition.
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
        var result = new List<BeamResolution>();

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
            if (measureStems.Length < 2)
                continue;

            var staffSpace = StaffSpaceFor(primitive, grid);
            if (staffSpace <= 1e-9)
                continue;

            var strip = TryBuildStrip(primitive.Contour, primitive.PhysicalBounds, staffSpace);
            if (strip is null)
                continue;

            var xTolerance = staffSpace * StemXTouchToleranceInStaffSpaces;
            var yTolerance = staffSpace * StemYTouchToleranceInStaffSpaces;

            var leftStem = FindTouchingStem(strip.LeftEndpoint, measureStems, xTolerance, yTolerance);
            if (leftStem is null)
                continue;

            var rightStem = FindTouchingStem(strip.RightEndpoint, measureStems, xTolerance, yTolerance, leftStem);
            if (rightStem is null)
                continue;

            result.Add(new BeamResolution(
                measureNumber,
                primitive.PhysicalBounds,
                strip.LeftEndpoint,
                strip.RightEndpoint,
                leftStem,
                rightStem));
        }

        return result
            .GroupBy(x => (
                x.MeasureNumber,
                LeftX: Math.Round(x.LeftEndpoint.X, 1),
                RightX: Math.Round(x.RightEndpoint.X, 1),
                LeftY: Math.Round(x.LeftEndpoint.Y, 1),
                RightY: Math.Round(x.RightEndpoint.Y, 1)))
            .Select(x => x.OrderByDescending(y => y.PhysicalBounds.Width).First())
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ThenBy(x => x.PhysicalBounds.Top)
            .ToArray();
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

        // Reject blobs/wedges whose two ends have wildly different local thickness.
        var minThickness = Math.Max(1e-9, Math.Min(leftThickness, rightThickness));
        var maxThickness = Math.Max(leftThickness, rightThickness);
        if (maxThickness / minThickness > 2.2)
            return null;

        var leftEndpoint = new PointD(bounds.Left, (leftMinY + leftMaxY) / 2.0);
        var rightEndpoint = new PointD(bounds.Right, (rightMinY + rightMaxY) / 2.0);
        return new StripCandidate(leftEndpoint, rightEndpoint);
    }

    private static StemResolution? FindTouchingStem(
        PointD endpoint,
        IReadOnlyList<StemResolution> stems,
        double xTolerance,
        double yTolerance,
        StemResolution? exclude = null)
    {
        return stems
            .Where(x => !ReferenceEquals(x, exclude))
            .Select(x => new
            {
                Stem = x,
                FreeEnd = FreeEndpoint(x),
            })
            .Where(x => Math.Abs(x.FreeEnd.X - endpoint.X) <= xTolerance)
            .Where(x => Math.Abs(x.FreeEnd.Y - endpoint.Y) <= yTolerance)
            .OrderBy(x => Distance(endpoint, x.FreeEnd))
            .Select(x => x.Stem)
            .FirstOrDefault();
    }

    private static PointD FreeEndpoint(StemResolution stem) =>
        stem.Direction == StemDirection.Up
            ? new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Top)
            : new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Bottom);

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

    private sealed record StripCandidate(PointD LeftEndpoint, PointD RightEndpoint);
}
