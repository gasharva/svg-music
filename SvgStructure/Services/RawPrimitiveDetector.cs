using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Detects raw SVG primitives belonging to one visual staff inside one measure.
///
/// The five staff-line fragments clipped to this staff-measure are injected as temporary
/// virtual seeds. From those lines and from real primitives intersecting the region we grow
/// incrementally up/down. A new primitive is accepted only when it is spatially close to an
/// already assigned primitive; a distant primitive elsewhere in the same measure can no
/// longer advance one global Y frontier.
/// </summary>
public sealed class RawPrimitiveDetector
{
    public double ProximityPercentOfMeasureHeight { get; init; } = 0.18;

    public IReadOnlySet<int> Detect(
        StaffMeasureRegion region,
        IReadOnlyList<RawPrimitive> primitives,
        double verticalTopLimit,
        double verticalBottomLimit,
        IReadOnlySet<int>? blockedPrimitiveIds = null)
    {
        blockedPrimitiveIds ??= new HashSet<int>();

        var working = primitives.ToList();
        working.AddRange(CreateVirtualStaffLines(region));

        var assigned = working
            .Where(x => x.Id < 0 || !blockedPrimitiveIds.Contains(x.Id))
            .Where(x => x.Bounds.Intersects(region.Bounds))
            .Select(x => x.Id)
            .ToHashSet();

        var maxGap = Math.Max(1, region.Height * ProximityPercentOfMeasureHeight);

        Grow(
            region,
            working,
            assigned,
            blockedPrimitiveIds,
            verticalTopLimit,
            verticalBottomLimit,
            maxGap);

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

    private static void Grow(
        StaffMeasureRegion region,
        IReadOnlyList<RawPrimitive> primitives,
        HashSet<int> assigned,
        IReadOnlySet<int> blockedPrimitiveIds,
        double topLimit,
        double bottomLimit,
        double maxGap)
    {
        while (true)
        {
            var assignedPrimitives = primitives
                .Where(x => assigned.Contains(x.Id))
                .ToList();

            var next = primitives
                .Where(x => x.Id >= 0)
                .Where(x => !assigned.Contains(x.Id))
                .Where(x => !blockedPrimitiveIds.Contains(x.Id))
                .Where(x => x.Bounds.IntersectsHorizontally(region.Left, region.Right))
                .Where(x => x.Bounds.Bottom >= topLimit && x.Bounds.Top <= bottomLimit)
                .Where(x => DistanceToCluster(x.Bounds, assignedPrimitives) <= maxGap)
                .OrderBy(x => DistanceToCluster(x.Bounds, assignedPrimitives))
                .ToList();

            if (next.Count == 0)
                return;

            foreach (var primitive in next)
                assigned.Add(primitive.Id);
        }
    }

    private static double DistanceToCluster(
        RectD candidate,
        IReadOnlyList<RawPrimitive> assigned)
    {
        if (assigned.Count == 0)
            return double.PositiveInfinity;

        return assigned.Min(x => RectangleDistance(candidate, x.Bounds));
    }

    private static double RectangleDistance(RectD a, RectD b)
    {
        var dx = a.Right < b.Left
            ? b.Left - a.Right
            : b.Right < a.Left
                ? a.Left - b.Right
                : 0;

        var dy = a.Bottom < b.Top
            ? b.Top - a.Bottom
            : b.Bottom < a.Top
                ? a.Top - b.Bottom
                : 0;

        return Math.Sqrt(dx * dx + dy * dy);
    }
}
