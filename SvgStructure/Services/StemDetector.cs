using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds thin vertical primitives that touch at least one recognized note head with one endpoint.
/// This is deliberately geometry-only: no PCA/glyph recognition is involved.
/// Cross-staff stems are often classified by PrimitiveResolver as measure-scoped rather than P+M,
/// so both scopes are intentionally considered here.
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
            if (primitive.MeasureNumber is null)
                continue;

            if (primitive.Scope is not (PrimitiveLogicalScope.PartMeasure or PrimitiveLogicalScope.Measure))
                continue;

            var measureNumber = primitive.MeasureNumber.Value;
            var measureBlocks = grid.Blocks
                .Where(x => x.MeasureNumber == measureNumber)
                .ToArray();
            if (measureBlocks.Length == 0)
                continue;

            var bounds = primitive.PhysicalBounds;
            if (bounds.Height <= 1e-9)
                continue;

            var widthToHeight = bounds.Width / bounds.Height;
            if (widthToHeight > MaxWidthToHeightRatio)
                continue;

            // Both staves in a grand staff use the same spacing. Average the available blocks so
            // measure-scoped cross-staff primitives do not need an artificial owning part yet.
            var staffSpace = measureBlocks.Average(x => x.PhysicalBounds.Height / 4.0);
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

            // A measure-scoped stem gets its logical owner from the note at the attached endpoint.
            // For a normal P+M primitive keep its original part where possible.
            var partNumber = primitive.PartNumber
                             ?? attached
                                 .OrderBy(x => EndpointDistance(
                                     bounds.CenterX,
                                     direction == StemDirection.Up ? bounds.Bottom : bounds.Top,
                                     x.PhysicalBounds))
                                 .Select(x => x.PartNumber)
                                 .First();

            if (!grid.TryGetBlock(partNumber, measureNumber, out var logicalBlock))
                continue;

            var crossStaff = IsCrossStaff(bounds, measureNumber, grid);
            var logicalBounds = logicalBlock.ToLogical(bounds);

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
