using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed class StaffSystemDetector
{
    // Geometry thresholds in this detector are deliberately dimensionless. Different SVG exporters
    // (and even different MuseScore page settings) use wildly different coordinate systems.
    private const double MinStaffLineWidthFraction = 0.35;
    private const double CoordinateToleranceFraction = 0.001;
    private const double StaffSpacingTolerance = 0.30;
    private const double MaxGapBetweenStaffsInSystemInStaffSpaces = 12.0;

    public IReadOnlyList<StaffSystem> Detect(IReadOnlyList<LineSegment> lines, RectD pageBounds)
    {
        var pageWidth = Math.Max(1e-9, pageBounds.Width);
        var pageHeight = Math.Max(1e-9, pageBounds.Height);
        var xTolerance = pageWidth * CoordinateToleranceFraction;
        var yTolerance = pageHeight * CoordinateToleranceFraction;
        var minStaffLineWidth = pageWidth * MinStaffLineWidthFraction;

        var horizontalCandidates = lines
            .Where(x => x.IsHorizontal(yTolerance) && x.Width >= minStaffLineWidth)
            .OrderBy(CenterY)
            .ToList();

        if (horizontalCandidates.Count < 5)
            return Array.Empty<StaffSystem>();

        var yGroups = GroupByY(horizontalCandidates, yTolerance);
        var staffSpacing = EstimateStaffSpacing(yGroups.Select(x => x.Y).ToList(), yTolerance);
        var staffs = DetectFiveLineStaffs(yGroups, staffSpacing);

        if (staffs.Count == 0)
            return Array.Empty<StaffSystem>();

        return GroupStaffsIntoSystems(staffs, staffSpacing)
            .Select(group => BuildSystem(group, lines, xTolerance, yTolerance))
            .Where(x => x is not null)
            .Cast<StaffSystem>()
            .OrderBy(x => x.Top)
            .ToList();
    }

    /// <summary>
    /// Finds actual five-line staves instead of assuming that every long horizontal line
    /// belongs to a staff. This is important for real SVGs containing page rectangles,
    /// long pedal lines and other unrelated horizontal geometry.
    /// </summary>
    private static IReadOnlyList<DetectedStaff> DetectFiveLineStaffs(
        IReadOnlyList<YLineGroup> groups,
        double spacing)
    {
        var result = new List<DetectedStaff>();
        var minGap = spacing * (1 - StaffSpacingTolerance);
        var maxGap = spacing * (1 + StaffSpacingTolerance);

        for (var i = 0; i <= groups.Count - 5;)
        {
            var candidate = groups.Skip(i).Take(5).ToArray();
            var gaps = candidate
                .Zip(candidate.Skip(1), (a, b) => b.Y - a.Y)
                .ToArray();

            if (gaps.All(gap => gap >= minGap && gap <= maxGap))
            {
                var lines = candidate
                    .Select(group => group.Lines.OrderByDescending(x => x.Width).First())
                    .ToList();

                result.Add(new DetectedStaff(
                    candidate.First().Y,
                    candidate.Last().Y,
                    lines));

                i += 5;
                continue;
            }

            i++;
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyList<DetectedStaff>> GroupStaffsIntoSystems(
        IReadOnlyList<DetectedStaff> staffs,
        double staffSpacing)
    {
        var result = new List<IReadOnlyList<DetectedStaff>>();
        var current = new List<DetectedStaff>();
        var maxGap = staffSpacing * MaxGapBetweenStaffsInSystemInStaffSpaces;

        foreach (var staff in staffs.OrderBy(x => x.Top))
        {
            if (current.Count > 0 && staff.Top - current[^1].Bottom > maxGap)
            {
                result.Add(current);
                current = new List<DetectedStaff>();
            }

            current.Add(staff);
        }

        if (current.Count > 0)
            result.Add(current);

        return result;
    }

    private static StaffSystem? BuildSystem(
        IReadOnlyList<DetectedStaff> detectedStaffs,
        IReadOnlyList<LineSegment> allLines,
        double xTolerance,
        double yTolerance)
    {
        if (detectedStaffs.Count == 0)
            return null;

        var staffLines = detectedStaffs.SelectMany(x => x.Lines).ToList();
        var staffs = detectedStaffs
            .Select((staff, partIndex) => new StaffBand(partIndex, staff.Top, staff.Bottom))
            .ToList();

        var left = staffLines.Average(x => x.Left);
        var right = staffLines.Average(x => x.Right);
        var top = detectedStaffs.Min(x => x.Top);
        var bottom = detectedStaffs.Max(x => x.Bottom);
        var requiredHeight = bottom - top - 2 * yTolerance;

        // Barlines are still deliberately expected to cross the entire grand staff. The only
        // change here is that equality/edge tolerances scale with the page instead of assuming
        // a particular SVG unit system.
        var barXs = Distinct(allLines
                .Where(x => x.IsVertical(xTolerance))
                .Where(x => x.Height >= requiredHeight)
                .Where(x => x.Left >= left - xTolerance && x.Left <= right + xTolerance)
                .Where(x => x.Top <= top + yTolerance && x.Bottom >= bottom - yTolerance)
                .Select(x => (x.Start.X + x.End.X) / 2), xTolerance)
            .OrderBy(x => x)
            .ToList();

        return barXs.Count >= 2
            ? new StaffSystem(left, right, top, bottom, staffs.Count, barXs, staffs)
            : null;
    }

    private static IReadOnlyList<YLineGroup> GroupByY(
        IReadOnlyList<LineSegment> lines,
        double tolerance)
    {
        var result = new List<YLineGroup>();

        foreach (var line in lines.OrderBy(CenterY))
        {
            var y = CenterY(line);
            var existing = result.LastOrDefault();

            if (existing is not null && Math.Abs(y - existing.Y) <= tolerance)
            {
                existing.Lines.Add(line);
                existing.Y = existing.Lines.Average(CenterY);
            }
            else
            {
                result.Add(new YLineGroup(y, new List<LineSegment> { line }));
            }
        }

        return result;
    }

    private static double EstimateStaffSpacing(IReadOnlyList<double> ys, double coordinateTolerance)
    {
        var gaps = ys
            .Zip(ys.Skip(1), (a, b) => b - a)
            .Where(x => x > coordinateTolerance)
            .OrderBy(x => x)
            .ToList();

        if (gaps.Count == 0)
            throw new InvalidOperationException("Could not estimate staff-line spacing.");

        // Staff-line gaps are among the smallest repeated gaps in the page. Using the
        // lower third keeps larger gaps between staves and systems out of the estimate.
        var sampleSize = Math.Max(1, gaps.Count / 3);
        var sample = gaps.Take(sampleSize).ToList();
        return sample[sample.Count / 2];
    }

    private static double CenterY(LineSegment line) =>
        (line.Start.Y + line.End.Y) / 2;

    private static IEnumerable<double> Distinct(IEnumerable<double> values, double tolerance)
    {
        double? previous = null;
        foreach (var value in values.OrderBy(x => x))
        {
            if (previous is null || Math.Abs(value - previous.Value) > tolerance)
            {
                yield return value;
                previous = value;
            }
        }
    }

    private sealed record DetectedStaff(
        double Top,
        double Bottom,
        IReadOnlyList<LineSegment> Lines);

    private sealed class YLineGroup(double y, List<LineSegment> lines)
    {
        public double Y { get; set; } = y;
        public List<LineSegment> Lines { get; } = lines;
    }
}
