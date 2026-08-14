using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Detects raw SVG primitives belonging to one visual staff inside one measure.
///
/// The five staff-line fragments clipped to this staff-measure are injected as temporary
/// virtual seeds. They participate only in neighbourhood growth and are never returned
/// as real SVG primitives.
/// </summary>
public sealed class RawPrimitiveDetector
{
    public double ProximityPercentOfMeasureHeight { get; init; } = 0.18;

    public IReadOnlySet<int> Detect(
        StaffMeasureRegion region,
        IReadOnlyList<RawPrimitive> primitives,
        double verticalTopLimit,
        double verticalBottomLimit)
    {
        var working = primitives.ToList();
        var virtualStaffLines = CreateVirtualStaffLines(region);
        working.AddRange(virtualStaffLines);

        var assigned = working
            .Where(x => x.Bounds.Intersects(region.Bounds))
            .Select(x => x.Id)
            .ToHashSet();

        var maxGap = Math.Max(1, region.Height * ProximityPercentOfMeasureHeight);

        GrowDown(region, working, assigned, verticalBottomLimit, maxGap);
        GrowUp(region, working, assigned, verticalTopLimit, maxGap);

        // Negative ids belong to temporary staff-line fragments only.
        return assigned.Where(x => x >= 0).ToHashSet();
    }

    private static IReadOnlyList<RawPrimitive> CreateVirtualStaffLines(StaffMeasureRegion region)
    {
        var result = new List<RawPrimitive>(5);
        var spacing = region.Height <= 0 ? 0 : region.Height / 4.0;
        var halfThickness = Math.Max(0.05, region.Height * 0.0025);

        for (var i = 0; i < 5; i++)
        {
            var y = region.Top + spacing * i;
            result.Add(new RawPrimitive(
                -1 - i,
                new RectD(
                    region.Left,
                    y - halfThickness,
                    region.Right,
                    y + halfThickness)));
        }

        return result;
    }

    private static void GrowDown(
        StaffMeasureRegion region,
        IReadOnlyList<RawPrimitive> primitives,
        HashSet<int> assigned,
        double bottomLimit,
        double maxGap)
    {
        while (true)
        {
            var assignedBottom = primitives
                .Where(x => assigned.Contains(x.Id))
                .Max(x => x.Bounds.Bottom);

            var next = primitives
                .Where(x => !assigned.Contains(x.Id))
                .Where(x => x.Id >= 0)
                .Where(x => x.Bounds.IntersectsHorizontally(region.Left, region.Right))
                .Where(x => x.Bounds.Top >= region.Bottom)
                .Where(x => x.Bounds.Top <= bottomLimit)
                .Where(x => x.Bounds.Top - assignedBottom <= maxGap)
                .OrderBy(x => x.Bounds.Top)
                .ToList();

            if (next.Count == 0)
                return;

            foreach (var primitive in next)
                assigned.Add(primitive.Id);
        }
    }

    private static void GrowUp(
        StaffMeasureRegion region,
        IReadOnlyList<RawPrimitive> primitives,
        HashSet<int> assigned,
        double topLimit,
        double maxGap)
    {
        while (true)
        {
            var assignedTop = primitives
                .Where(x => assigned.Contains(x.Id))
                .Min(x => x.Bounds.Top);

            var next = primitives
                .Where(x => !assigned.Contains(x.Id))
                .Where(x => x.Id >= 0)
                .Where(x => x.Bounds.IntersectsHorizontally(region.Left, region.Right))
                .Where(x => x.Bounds.Bottom <= region.Top)
                .Where(x => x.Bounds.Bottom >= topLimit)
                .Where(x => assignedTop - x.Bounds.Bottom <= maxGap)
                .OrderByDescending(x => x.Bounds.Bottom)
                .ToList();

            if (next.Count == 0)
                return;

            foreach (var primitive in next)
                assigned.Add(primitive.Id);
        }
    }
}
