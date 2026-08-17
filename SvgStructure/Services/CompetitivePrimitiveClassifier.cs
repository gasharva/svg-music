using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Propagates staff-measure ownership competitively.
///
/// Only primitives that already belong to exactly one staff-measure may propagate that color.
/// Physical touching is stronger than ordinary proximity. If an unassigned primitive touches one
/// color, that color wins before the broader proximity pass is considered.
/// </summary>
public sealed class CompetitivePrimitiveClassifier
{
    public double ProximityInStaffSpaces { get; init; } = 1.5;
    public double MinimumDirectVerticalOverlapRatio { get; init; } = 0.40;

    public IReadOnlyDictionary<int, HashSet<StaffMeasureKey>> Classify(
        IReadOnlyList<RawPrimitive> primitives,
        IReadOnlyList<StaffMeasureRegion> regions,
        RectD pageBounds)
    {
        var limitsByKey = regions.ToDictionary(
            x => x.Key,
            x => GetVerticalLimits(x, regions, pageBounds));

        var assigned = new Dictionary<int, StaffMeasureKey>();
        var ambiguous = new HashSet<int>();
        var pending = primitives.Select(x => x.Id).ToHashSet();

        // A mere edge intersection with a staff is not enough to make a primitive a strong seed.
        // Long stems and other objects between the two staves may just graze the neighbouring staff.
        // Direct ownership is granted only when the primitive's vertical center lies in the staff,
        // or a substantial fraction of its own height overlaps that staff.
        foreach (var primitive in primitives)
        {
            var intersectingRegions = regions
                .Where(x => primitive.Bounds.Intersects(x.Bounds))
                .ToArray();

            var substantialRegions = intersectingRegions
                .Where(region => IsSubstantialDirectOverlap(primitive.Bounds, region))
                .ToArray();

            var directKeys = substantialRegions
                .Select(x => x.Key)
                .Distinct()
                .ToArray();

            if (directKeys.Length == 1)
            {
                assigned[primitive.Id] = directKeys[0];
                pending.Remove(primitive.Id);
            }
            else if (directKeys.Length > 1)
            {
                ambiguous.Add(primitive.Id);
                pending.Remove(primitive.Id);
            }
        }

        // Virtual fragments of the five staff lines are permanent, unambiguous seeds.
        var virtualSeeds = regions.ToDictionary(
            x => x.Key,
            x => CreateVirtualStaffLines(x));

        var primitiveById = primitives.ToDictionary(x => x.Id);

        while (true)
        {
            var newlyAssigned = new Dictionary<int, StaffMeasureKey>();
            var newlyAmbiguous = new HashSet<int>();

            // Work from a snapshot. Objects assigned during this round only start propagating
            // in the next round, so iteration order cannot choose the winner.
            var realSeedsByKey = assigned
                .GroupBy(x => x.Value)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(y => primitiveById[y.Key]).ToArray());

            foreach (var primitiveId in pending)
            {
                var primitive = primitiveById[primitiveId];
                var touchingColors = new HashSet<StaffMeasureKey>();
                var nearbyColors = new HashSet<StaffMeasureKey>();

                foreach (var region in regions)
                {
                    var overlapsHorizontally = primitive.Bounds.IntersectsHorizontally(region.Left, region.Right);
                    if (!overlapsHorizontally)
                        continue;

                    var limits = limitsByKey[region.Key];
                    var insideVerticalLimits =
                        primitive.Bounds.Bottom >= limits.Top &&
                        primitive.Bounds.Top <= limits.Bottom;
                    if (!insideVerticalLimits)
                        continue;

                    var staffSpace = region.Height <= 0 ? 0 : region.Height / 4.0;
                    var maxGap = staffSpace * ProximityInStaffSpaces;

                    var touchesColor = TouchesColor(
                        primitive.Bounds,
                        virtualSeeds[region.Key],
                        realSeedsByKey.GetValueOrDefault(region.Key));

                    if (touchesColor)
                    {
                        touchingColors.Add(region.Key);
                        continue;
                    }

                    var closeToColor = IsCloseToColor(
                        primitive.Bounds,
                        virtualSeeds[region.Key],
                        realSeedsByKey.GetValueOrDefault(region.Key),
                        maxGap);

                    if (closeToColor)
                        nearbyColors.Add(region.Key);
                }

                if (touchingColors.Count == 1)
                {
                    newlyAssigned[primitiveId] = touchingColors.Single();
                    continue;
                }

                if (touchingColors.Count > 1)
                {
                    newlyAmbiguous.Add(primitiveId);
                    continue;
                }

                if (nearbyColors.Count == 1)
                {
                    newlyAssigned[primitiveId] = nearbyColors.Single();
                }
                else if (nearbyColors.Count > 1)
                {
                    newlyAmbiguous.Add(primitiveId);
                }
            }

            if (newlyAssigned.Count == 0 && newlyAmbiguous.Count == 0)
                break;

            foreach (var (id, key) in newlyAssigned)
            {
                assigned[id] = key;
                pending.Remove(id);
            }

            foreach (var id in newlyAmbiguous)
            {
                ambiguous.Add(id);
                pending.Remove(id);
            }
        }

        var result = new Dictionary<int, HashSet<StaffMeasureKey>>();

        foreach (var (id, key) in assigned)
            result[id] = new HashSet<StaffMeasureKey> { key };

        // A set with several keys is only a rendering marker for gray/unclassified.
        // These ids were removed from propagation as soon as ambiguity was detected.
        foreach (var id in ambiguous)
            result[id] = new HashSet<StaffMeasureKey>(FindRelevantKeys(primitiveById[id], regions));

        return result;
    }

    private bool IsSubstantialDirectOverlap(RectD primitive, StaffMeasureRegion region)
    {
        var primitiveHeight = Math.Max(1e-9, primitive.Height);
        var overlapTop = Math.Max(primitive.Top, region.Top);
        var overlapBottom = Math.Min(primitive.Bottom, region.Bottom);
        var overlapHeight = Math.Max(0, overlapBottom - overlapTop);
        var overlapRatio = overlapHeight / primitiveHeight;

        var centerInside =
            primitive.CenterY >= region.Top &&
            primitive.CenterY <= region.Bottom;

        return centerInside || overlapRatio >= MinimumDirectVerticalOverlapRatio;
    }

    private static bool TouchesColor(
        RectD candidate,
        IReadOnlyList<RectD> virtualSeeds,
        IReadOnlyList<RawPrimitive>? realSeeds)
    {
        var touchesVirtualSeed = virtualSeeds
            .Any(x => RectangleDistance(candidate, x) <= 1e-9);
        if (touchesVirtualSeed)
            return true;

        if (realSeeds is null)
            return false;

        return realSeeds.Any(x => RectangleDistance(candidate, x.Bounds) <= 1e-9);
    }

    private static bool IsCloseToColor(
        RectD candidate,
        IReadOnlyList<RectD> virtualSeeds,
        IReadOnlyList<RawPrimitive>? realSeeds,
        double maxGap)
    {
        var closeToVirtualSeed = virtualSeeds
            .Any(x => RectangleDistance(candidate, x) <= maxGap);
        if (closeToVirtualSeed)
            return true;

        if (realSeeds is null)
            return false;

        return realSeeds.Any(x => RectangleDistance(candidate, x.Bounds) <= maxGap);
    }

    private static IReadOnlyList<RectD> CreateVirtualStaffLines(StaffMeasureRegion region)
    {
        var result = new List<RectD>(5);
        var spacing = region.Height <= 0 ? 0 : region.Height / 4.0;
        var halfThickness = Math.Max(0.05, region.Height * 0.0025);

        for (var i = 0; i < 5; i++)
        {
            var y = region.Top + spacing * i;
            result.Add(new RectD(
                region.Left,
                y - halfThickness,
                region.Right,
                y + halfThickness));
        }

        return result;
    }

    private static IReadOnlyCollection<StaffMeasureKey> FindRelevantKeys(
        RawPrimitive primitive,
        IReadOnlyList<StaffMeasureRegion> regions)
    {
        var horizontallyOverlapping = regions
            .Where(x => primitive.Bounds.IntersectsHorizontally(x.Left, x.Right))
            .ToArray();

        var keys = horizontallyOverlapping
            .Select(x => x.Key)
            .Distinct()
            .Take(2)
            .ToArray();

        // Rendering only checks Count == 1. Ensure ambiguous stays visibly gray even in the
        // unlikely case that only one horizontally overlapping region was found.
        return keys.Length >= 2
            ? keys
            : new[] { new StaffMeasureKey(-1, -1), new StaffMeasureKey(-2, -2) };
    }

    private static (double Top, double Bottom) GetVerticalLimits(
        StaffMeasureRegion region,
        IReadOnlyList<StaffMeasureRegion> regions,
        RectD pageBounds)
    {
        var otherRegions = regions
            .Where(x => x.Key != region.Key || x.SystemIndex != region.SystemIndex)
            .ToArray();

        var horizontallyOverlapping = otherRegions
            .Where(x => HorizontallyOverlaps(x, region))
            .ToArray();

        var above = horizontallyOverlapping
            .Where(x => x.Bottom <= region.Top)
            .OrderByDescending(x => x.Bottom)
            .FirstOrDefault();

        var below = horizontallyOverlapping
            .Where(x => x.Top >= region.Bottom)
            .OrderBy(x => x.Top)
            .FirstOrDefault();

        return (above?.Bottom ?? pageBounds.Top, below?.Top ?? pageBounds.Bottom);
    }

    private static bool HorizontallyOverlaps(StaffMeasureRegion a, StaffMeasureRegion b) =>
        a.Right > b.Left && a.Left < b.Right;

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
