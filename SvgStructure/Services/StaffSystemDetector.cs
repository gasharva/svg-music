using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed class StaffSystemDetector
{
    // Geometry thresholds in this detector are deliberately dimensionless. Different SVG exporters
    // (and even different MuseScore page settings) use wildly different coordinate systems.
    private const double MinStaffLineWidthFraction = 0.35;
    private const double CoordinateToleranceFraction = 0.001;
    private const double StaffSpacingTolerance = 0.30;
    private const double BarlineEdgeToleranceInStaffSpaces = 0.25;
    private const double VerticalSegmentJoinToleranceInStaffSpaces = 0.10;

    public IReadOnlyList<StaffSystem> Detect(IReadOnlyList<LineSegment> lines, RectD pageBounds)
    {
        var pageWidth = Math.Max(1e-9, pageBounds.Width);
        var pageHeight = Math.Max(1e-9, pageBounds.Height);
        var xTolerance = pageWidth * CoordinateToleranceFraction;
        var yTolerance = pageHeight * CoordinateToleranceFraction;
        var minStaffLineWidth = pageWidth * MinStaffLineWidthFraction;

        // Keep filtering stages separate on purpose: these intermediate collections are useful
        // breakpoints/watch targets when a new SVG layout stops being detected.
        var horizontalLines = lines
            .Where(x => x.IsHorizontal(yTolerance))
            .ToList();

        var wideHorizontalLines = horizontalLines
            .Where(x => x.Width >= minStaffLineWidth)
            .ToList();

        var horizontalCandidates = wideHorizontalLines
            .OrderBy(CenterY)
            .ToList();

        if (horizontalCandidates.Count < 5)
            return Array.Empty<StaffSystem>();

        var yGroups = GroupByY(horizontalCandidates, yTolerance);
        var staffSpacing = EstimateStaffSpacing(yGroups.Select(x => x.Y).ToList(), yTolerance);
        var staffs = DetectFiveLineStaffs(yGroups, staffSpacing);

        if (staffs.Count < 2)
            return Array.Empty<StaffSystem>();

        // Temporary but deterministic score model: every input system is a grand staff made of
        // exactly two consecutive five-line staves. Do not infer system membership from page
        // geometry here; that made the detector depend too heavily on engraving/page layout.
        var grandStaffGroups = PairConsecutiveStaffs(staffs);

        var systems = grandStaffGroups
            .Select(group => BuildSystem(group, lines, staffSpacing, xTolerance, yTolerance))
            .ToList();

        var validSystems = systems
            .Where(x => x is not null)
            .Cast<StaffSystem>()
            .ToList();

        return validSystems
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

    private static IReadOnlyList<IReadOnlyList<DetectedStaff>> PairConsecutiveStaffs(
        IReadOnlyList<DetectedStaff> staffs)
    {
        var ordered = staffs.OrderBy(x => x.Top).ToArray();
        var result = new List<IReadOnlyList<DetectedStaff>>(ordered.Length / 2);

        // By current pipeline contract an input page contains only two-staff grand staffs.
        // If a malformed/unsupported page leaves an odd staff at the end, do not invent a system
        // for it: BuildSystem expects a complete grand staff and its spanning barlines.
        for (var i = 0; i + 1 < ordered.Length; i += 2)
            result.Add(new[] { ordered[i], ordered[i + 1] });

        return result;
    }

    private static StaffSystem? BuildSystem(
        IReadOnlyList<DetectedStaff> detectedStaffs,
        IReadOnlyList<LineSegment> allLines,
        double staffSpacing,
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

        // MuseScore/exporters may draw a grand-staff barline a little inside the outer staff-line
        // centres. Treat a quarter of one staff space at each edge as equivalent to spanning the
        // whole grand staff. This remains scale-independent and still rejects materially shorter
        // vertical symbols/stems.
        var barlineEdgeTolerance = Math.Max(
            yTolerance,
            staffSpacing * BarlineEdgeToleranceInStaffSpaces);
        var requiredHeight = bottom - top - 2 * barlineEdgeTolerance;

        // Keep every filter as a named stage. Besides being easier to read, this makes it trivial
        // to see exactly which condition rejects geometry when debugging a new engraving style.
        var verticalLines = allLines
            .Where(x => x.IsVertical(xTolerance))
            .ToList();

        // MuseScore 4.6 may emit one visual grand-staff barline as two adjacent SVG polylines:
        // a long segment down to the upper edge of the lower staff, then a shorter continuation
        // through the lower staff. SvgSceneGeometryReader quite correctly returns those as two
        // LineSegments, so reconstruct the visual vertical line before applying height tests.
        var verticalSegmentJoinTolerance = Math.Max(
            yTolerance,
            staffSpacing * VerticalSegmentJoinToleranceInStaffSpaces);

        var mergedVerticalLines = MergeCollinearVerticalSegments(
            verticalLines,
            xTolerance,
            verticalSegmentJoinTolerance);

        var highEnoughLines = mergedVerticalLines
            .Where(x => x.Height >= requiredHeight)
            .ToList();

        var horizontallyInsideStaff = highEnoughLines
            .Where(x => x.Left >= left - xTolerance && x.Left <= right + xTolerance)
            .ToList();

        var spanningGrandStaff = horizontallyInsideStaff
            .Where(x =>
                x.Top <= top + barlineEdgeTolerance &&
                x.Bottom >= bottom - barlineEdgeTolerance)
            .ToList();

        var barXValues = spanningGrandStaff
            .Select(x => (x.Start.X + x.End.X) / 2)
            .ToList();

        var distinctBarXs = Distinct(barXValues, xTolerance)
            .ToList();

        var barXs = distinctBarXs
            .OrderBy(x => x)
            .ToList();

        return barXs.Count >= 2
            ? new StaffSystem(left, right, top, bottom, staffs.Count, barXs, staffs)
            : null;
    }

    private static IReadOnlyList<LineSegment> MergeCollinearVerticalSegments(
        IReadOnlyList<LineSegment> lines,
        double xTolerance,
        double yJoinTolerance)
    {
        var ordered = lines
            .Select(x => new
            {
                Line = x,
                X = (x.Start.X + x.End.X) / 2
            })
            .OrderBy(x => x.X)
            .ThenBy(x => x.Line.Top)
            .ToList();

        var xGroups = new List<List<LineSegment>>();
        var xGroupCenters = new List<double>();

        foreach (var item in ordered)
        {
            var groupIndex = -1;
            for (var i = xGroupCenters.Count - 1; i >= 0; i--)
            {
                if (item.X - xGroupCenters[i] > xTolerance)
                    break;

                if (Math.Abs(item.X - xGroupCenters[i]) <= xTolerance)
                {
                    groupIndex = i;
                    break;
                }
            }

            if (groupIndex < 0)
            {
                xGroups.Add(new List<LineSegment> { item.Line });
                xGroupCenters.Add(item.X);
                continue;
            }

            xGroups[groupIndex].Add(item.Line);
            xGroupCenters[groupIndex] = xGroups[groupIndex]
                .Average(x => (x.Start.X + x.End.X) / 2);
        }

        var result = new List<LineSegment>();

        foreach (var xGroup in xGroups)
        {
            var byY = xGroup
                .OrderBy(x => x.Top)
                .ToList();

            if (byY.Count == 0)
                continue;

            var x = byY.Average(line => (line.Start.X + line.End.X) / 2);
            var currentTop = byY[0].Top;
            var currentBottom = byY[0].Bottom;

            for (var i = 1; i < byY.Count; i++)
            {
                var next = byY[i];

                if (next.Top <= currentBottom + yJoinTolerance)
                {
                    currentBottom = Math.Max(currentBottom, next.Bottom);
                    continue;
                }

                result.Add(new LineSegment(
                    new PointD(x, currentTop),
                    new PointD(x, currentBottom)));

                currentTop = next.Top;
                currentBottom = next.Bottom;
            }

            result.Add(new LineSegment(
                new PointD(x, currentTop),
                new PointD(x, currentBottom)));
        }

        return result;
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
