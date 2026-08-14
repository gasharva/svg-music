using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed class StaffSystemDetector
{
    private const double MinStaffLineWidth = 300;
    private const double CoordinateTolerance = 0.75;

    public IReadOnlyList<StaffSystem> Detect(IReadOnlyList<LineSegment> lines)
    {
        var staffLines = lines
            .Where(x => x.IsHorizontal() && x.Width >= MinStaffLineWidth)
            .OrderBy(CenterY)
            .ToList();

        if (staffLines.Count < 5)
            return Array.Empty<StaffSystem>();

        var staffYs = Distinct(staffLines.Select(CenterY)).ToList();
        var staffSpacing = EstimateStaffSpacing(staffYs);
        var maxGapInsideSystem = staffSpacing * 10;

        return SplitIntoSystems(staffLines, maxGapInsideSystem)
            .Select(group => BuildSystem(group, lines))
            .Where(x => x is not null)
            .Cast<StaffSystem>()
            .OrderBy(x => x.Top)
            .ToList();
    }

    private static IReadOnlyList<IReadOnlyList<LineSegment>> SplitIntoSystems(
        IReadOnlyList<LineSegment> staffLines,
        double maxGapInsideSystem)
    {
        var result = new List<IReadOnlyList<LineSegment>>();
        var current = new List<LineSegment>();
        double? previousY = null;

        foreach (var line in staffLines)
        {
            var y = CenterY(line);

            if (previousY is not null && y - previousY.Value > maxGapInsideSystem)
            {
                if (current.Count > 0)
                    result.Add(current);

                current = new List<LineSegment>();
            }

            current.Add(line);
            previousY = y;
        }

        if (current.Count > 0)
            result.Add(current);

        return result;
    }

    private static StaffSystem? BuildSystem(
        IReadOnlyList<LineSegment> staffLines,
        IReadOnlyList<LineSegment> allLines)
    {
        var ys = Distinct(staffLines.Select(CenterY)).ToList();
        if (ys.Count < 5 || ys.Count % 5 != 0)
            return null;

        var staffCount = ys.Count / 5;
        var staffs = Enumerable.Range(0, staffCount)
            .Select(partIndex =>
            {
                var staffYs = ys.Skip(partIndex * 5).Take(5).ToArray();
                return new StaffBand(partIndex, staffYs.First(), staffYs.Last());
            })
            .ToList();

        var left = staffLines.Average(x => x.Left);
        var right = staffLines.Average(x => x.Right);
        var top = ys.Min();
        var bottom = ys.Max();
        var requiredHeight = bottom - top - 2;

        var barXs = Distinct(allLines
                .Where(x => x.IsVertical())
                .Where(x => x.Height >= requiredHeight)
                .Where(x => x.Left >= left - 1 && x.Left <= right + 1)
                .Where(x => x.Top <= top + 1 && x.Bottom >= bottom - 1)
                .Select(x => (x.Start.X + x.End.X) / 2))
            .OrderBy(x => x)
            .ToList();

        return barXs.Count >= 2
            ? new StaffSystem(left, right, top, bottom, staffCount, barXs, staffs)
            : null;
    }

    private static double EstimateStaffSpacing(IReadOnlyList<double> ys)
    {
        var gaps = ys
            .Zip(ys.Skip(1), (a, b) => b - a)
            .Where(x => x > CoordinateTolerance)
            .OrderBy(x => x)
            .ToList();

        if (gaps.Count == 0)
            throw new InvalidOperationException("Could not estimate staff-line spacing.");

        var sampleSize = Math.Max(1, gaps.Count / 3);
        var sample = gaps.Take(sampleSize).ToList();
        return sample[sample.Count / 2];
    }

    private static double CenterY(LineSegment line) =>
        (line.Start.Y + line.End.Y) / 2;

    private static IEnumerable<double> Distinct(IEnumerable<double> values)
    {
        double? previous = null;
        foreach (var value in values.OrderBy(x => x))
        {
            if (previous is null || Math.Abs(value - previous.Value) > CoordinateTolerance)
            {
                yield return value;
                previous = value;
            }
        }
    }
}
