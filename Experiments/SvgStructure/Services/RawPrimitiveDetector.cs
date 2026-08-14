using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Detects raw SVG primitives belonging to one measure.
///
/// 1. Seed with every primitive that intersects the measure rectangle.
/// 2. Extend only vertically, never left/right outside the measure X range.
/// 3. Walk down and up incrementally: a primitive is added only when it is close
///    to something already assigned to this measure.
/// </summary>
public sealed class RawPrimitiveDetector
{
    public double ProximityPercentOfMeasureHeight { get; init; } = 0.18;

    public IReadOnlySet<int> Detect(
        MeasureRegion measure,
        IReadOnlyList<RawPrimitive> primitives,
        double verticalTopLimit,
        double verticalBottomLimit)
    {
        var assigned = primitives
            .Where(x => x.Bounds.Intersects(measure.Bounds))
            .Select(x => x.Id)
            .ToHashSet();

        if (assigned.Count == 0)
            return assigned;

        var maxGap = Math.Max(1, measure.Height * ProximityPercentOfMeasureHeight);

        GrowDown(measure, primitives, assigned, verticalBottomLimit, maxGap);
        GrowUp(measure, primitives, assigned, verticalTopLimit, maxGap);

        return assigned;
    }

    private static void GrowDown(
        MeasureRegion measure,
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
                .Where(x => x.Bounds.IntersectsHorizontally(measure.Left, measure.Right))
                .Where(x => x.Bounds.Top >= measure.Bottom)
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
        MeasureRegion measure,
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
                .Where(x => x.Bounds.IntersectsHorizontally(measure.Left, measure.Right))
                .Where(x => x.Bounds.Bottom <= measure.Top)
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
