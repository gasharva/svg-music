using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Removes obviously non-musical scene objects from the primitive classification pass.
/// A primitive is considered garbage only when it is large in BOTH dimensions relative
/// to the detected measure grid (for example a page/system background rectangle).
/// Long thin staff lines and barlines therefore survive this filter.
/// </summary>
public sealed class GarbageCleaner
{
    public double MaxMeasureWidths { get; init; } = 1.8;
    public double MaxMeasureHeights { get; init; } = 1.8;

    public GarbageCleanupResult Clean(
        IReadOnlyList<RawPrimitive> primitives,
        IReadOnlyList<MeasureRegion> measures)
    {
        if (measures.Count == 0)
            return new GarbageCleanupResult(primitives, new HashSet<int>());

        var typicalWidth = Median(measures.Select(x => x.Right - x.Left));
        var typicalHeight = Median(measures.Select(x => x.Height));

        var garbageIds = new HashSet<int>();
        var kept = new List<RawPrimitive>(primitives.Count);

        foreach (var primitive in primitives)
        {
            var oversizedHorizontally = primitive.Bounds.Width > typicalWidth * MaxMeasureWidths;
            var oversizedVertically = primitive.Bounds.Height > typicalHeight * MaxMeasureHeights;

            if (oversizedHorizontally && oversizedVertically)
                garbageIds.Add(primitive.Id);
            else
                kept.Add(primitive);
        }

        return new GarbageCleanupResult(kept, garbageIds);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values
            .Where(x => x > 0)
            .OrderBy(x => x)
            .ToArray();

        if (ordered.Length == 0)
            return 1;

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }
}

public sealed record GarbageCleanupResult(
    IReadOnlyList<RawPrimitive> Primitives,
    IReadOnlySet<int> GarbageIds);
