using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds thin vertical primitives that touch at least one recognized note head with one endpoint.
/// This is deliberately geometry-only: no PCA/glyph recognition is involved.
/// </summary>
public sealed class StemDetector
{
    public double MaxWidthToHeightRatio { get; init; } = 0.16;
    public double MinHeightInStaffSpaces { get; init; } = 1.20;
    public double EndpointTouchToleranceInStaffSpaces { get; init; } = 0.28;
    public double HorizontalTouchToleranceInStaffSpaces { get; init; } = 0.20;

    public IReadOnlyList<StemResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads)
    {
        var result = new List<StemResolution>();

        foreach (var primitive in primitives.Primitives)
        {
            if (primitive.Scope != PrimitiveLogicalScope.PartMeasure ||
                primitive.PartNumber is null ||
                primitive.MeasureNumber is null)
                continue;

            var partNumber = primitive.PartNumber.Value;
            var measureNumber = primitive.MeasureNumber.Value;
            if (!grid.TryGetBlock(partNumber, measureNumber, out var block))
                continue;

            var bounds = primitive.PhysicalBounds;
            if (bounds.Height <= 1e-9)
                continue;

            var widthToHeight = bounds.Width / bounds.Height;
            if (widthToHeight > MaxWidthToHeightRatio)
                continue;

            var staffSpace = block.PhysicalBounds.Height / 4.0;
            if (staffSpace <= 1e-9)
                continue;

            if (bounds.Height / staffSpace < MinHeightInStaffSpaces)
                continue;

            var touchY = staffSpace * EndpointTouchToleranceInStaffSpaces;
            var touchX = staffSpace * HorizontalTouchToleranceInStaffSpaces;

            var measureNotes = noteHeads
                .Where(x => x.MeasureNumber == measureNumber)
                .ToArray();

            var topMatches = measureNotes
                .Where(x => EndpointTouchesHead(bounds.CenterX, bounds.Top, x.PhysicalBounds, touchX, touchY))
                .ToArray();

            var bottomMatches = measureNotes
                .Where(x => EndpointTouchesHead(bounds.CenterX, bounds.Bottom, x.PhysicalBounds, touchX, touchY))
                .ToArray();

            if (topMatches.Length == 0 && bottomMatches.Length == 0)
                continue;

            StemDirection direction;
            IReadOnlyList<NoteHeadResolution> attached;

            if (bottomMatches.Length > 0 && topMatches.Length == 0)
            {
                direction = StemDirection.Up;
                attached = bottomMatches;
            }
            else if (topMatches.Length > 0 && bottomMatches.Length == 0)
            {
                direction = StemDirection.Down;
                attached = topMatches;
            }
            else
            {
                var topDistance = topMatches.Min(x => EndpointDistance(bounds.CenterX, bounds.Top, x.PhysicalBounds));
                var bottomDistance = bottomMatches.Min(x => EndpointDistance(bounds.CenterX, bounds.Bottom, x.PhysicalBounds));
                if (bottomDistance <= topDistance)
                {
                    direction = StemDirection.Up;
                    attached = bottomMatches;
                }
                else
                {
                    direction = StemDirection.Down;
                    attached = topMatches;
                }
            }

            var crossStaff = IsCrossStaff(bounds, measureNumber, grid);
            var logicalBounds = block.ToLogical(bounds);

            result.Add(new StemResolution(
                partNumber,
                measureNumber,
                logicalBounds,
                bounds,
                direction,
                crossStaff,
                attached));
        }

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ThenBy(x => x.PhysicalBounds.Top)
            .ToArray();
    }

    private static bool EndpointTouchesHead(
        double x,
        double y,
        RectD head,
        double xTolerance,
        double yTolerance)
    {
        var xMatches = x >= head.Left - xTolerance && x <= head.Right + xTolerance;
        var yMatches = y >= head.Top - yTolerance && y <= head.Bottom + yTolerance;
        return xMatches && yMatches;
    }

    private static double EndpointDistance(double x, double y, RectD head)
    {
        var dx = x < head.Left
            ? head.Left - x
            : x > head.Right
                ? x - head.Right
                : 0.0;

        var dy = y < head.Top
            ? head.Top - y
            : y > head.Bottom
                ? y - head.Bottom
                : 0.0;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsCrossStaff(
        RectD stem,
        int measureNumber,
        LogicalGridResolution grid)
    {
        var touchedParts = grid.Blocks
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => VerticalIntervalsIntersect(stem, x.PhysicalBounds))
            .Select(x => x.PartNumber)
            .Distinct()
            .Take(2)
            .Count();

        return touchedParts >= 2;
    }

    private static bool VerticalIntervalsIntersect(RectD a, RectD b) =>
        a.Bottom >= b.Top && a.Top <= b.Bottom;
}
