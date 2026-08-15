using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds the real SVG contours that represent long staff lines.
/// They are structural helpers, not musical primitives belonging to one staff-measure,
/// so the classifier must ignore them and keep their original appearance.
/// </summary>
public sealed class StaffLinePrimitiveDetector
{
    public double MinRegionWidths { get; init; } = 1.25;
    public double MaxThicknessPercentOfStaffHeight { get; init; } = 0.04;
    public double YTolerancePercentOfStaffHeight { get; init; } = 0.05;

    public IReadOnlySet<int> Detect(
        IReadOnlyList<RawPrimitive> primitives,
        IReadOnlyList<StaffMeasureRegion> regions)
    {
        if (regions.Count == 0)
            return new HashSet<int>();

        var typicalWidth = Median(regions.Select(x => x.Right - x.Left));
        var typicalHeight = Median(regions.Select(x => x.Height));
        var maxThickness = Math.Max(0.75, typicalHeight * MaxThicknessPercentOfStaffHeight);
        var yTolerance = Math.Max(0.75, typicalHeight * YTolerancePercentOfStaffHeight);

        var staffYs = regions
            .SelectMany(GetStaffLineYs)
            .OrderBy(x => x)
            .ToArray();

        return primitives
            .Where(x => x.Bounds.Width >= typicalWidth * MinRegionWidths)
            .Where(x => x.Bounds.Height <= maxThickness)
            .Where(x => staffYs.Any(y => Math.Abs(x.Bounds.CenterY - y) <= yTolerance))
            .Select(x => x.Id)
            .ToHashSet();
    }

    private static IEnumerable<double> GetStaffLineYs(StaffMeasureRegion region)
    {
        if (region.Height <= 0)
        {
            yield return region.Top;
            yield break;
        }

        var spacing = region.Height / 4.0;
        for (var i = 0; i < 5; i++)
            yield return region.Top + spacing * i;
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Where(x => x > 0).OrderBy(x => x).ToArray();
        if (ordered.Length == 0)
            return 1;

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }
}
