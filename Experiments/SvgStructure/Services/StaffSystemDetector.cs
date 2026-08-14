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
            .ToList();

        return staffLines
            .GroupBy(x => new EndpointKey(Bucket(x.Left), Bucket(x.Right)))
            .Where(g => g.Count() >= 5)
            .Select(g => BuildSystem(g.ToList(), lines))
            .Where(x => x is not null)
            .Cast<StaffSystem>()
            .OrderBy(x => x.Top)
            .ToList();
    }

    private static StaffSystem? BuildSystem(
        IReadOnlyList<LineSegment> staffLines,
        IReadOnlyList<LineSegment> allLines)
    {
        var ys = Distinct(staffLines.Select(x => (x.Start.Y + x.End.Y) / 2)).ToList();
        if (ys.Count < 5 || ys.Count % 5 != 0)
            return null;

        var staffCount = ys.Count / 5;
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
            ? new StaffSystem(left, right, top, bottom, staffCount, barXs)
            : null;
    }

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

    private static long Bucket(double value) =>
        (long)Math.Round(value / CoordinateTolerance);

    private readonly record struct EndpointKey(long Left, long Right);
}
