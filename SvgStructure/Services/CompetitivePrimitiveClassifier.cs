using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Propagates staff-measure ownership competitively.
///
/// Only primitives that already belong to exactly one staff-measure may propagate that color.
/// Thin vertical primitives are a special case: they may span a large empty area between staves,
/// so ordinary proximity is unsafe for them. They are expected to inherit ownership primarily
/// from geometry they actually touch or intersect.
/// </summary>
public sealed class CompetitivePrimitiveClassifier
{
    public double ProximityInStaffSpaces { get; init; } = 1.5;

    // A very thin/tall primitive is typically a stem or another vertical connector. Its nearest
    // endpoint may be close to the wrong staff even though the primitive logically belongs to a
    // cluster at the opposite end. Do not let such geometry propagate over normal staff-space gaps.
    public double ThinVerticalMaxWidthToHeightRatio { get; init; } = 0.12;
    public double ThinVerticalProximityInStaffSpaces { get; init; } = 0.05;

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

        // Initial real anchors: physical intersection with exactly one staff-measure.
        // Intersections with several regions are ambiguous immediately and never propagate.
        foreach (var primitive in primitives)
        {
            var directRegions = regions
                .Where(x => primitive.Bounds.Intersects(x.Bounds))
                .ToArray();

            var directKeys = directRegions
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
                var nearbyColors = new HashSet<StaffMeasureKey>();
                var thinVertical = IsThinVertical(primitive.Bounds);

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
                    var proximityInStaffSpaces = thinVertical
                        ? ThinVerticalProximityInStaffSpaces
                        : ProximityInStaffSpaces;
                    var maxGap = staffSpace * proximityInStaffSpaces;

                    var closeToColor = IsCloseToColor(
                        primitive.Bounds,
                        virtualSeeds[region.Key],
                        realSeedsByKey.GetValueOrDefault(region.Key),
                        maxGap);

                    if (closeToColor)
                        nearbyColors.Add(region.Key);
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

    private bool IsThinVertical(RectD bounds)
    {
        if (bounds.Height <= 1e-9)
            return false;

        if (bounds.Height <= bounds.Width)
            return false;

        var widthToHeightRatio = bounds.Width / bounds.Height;
        return widthToHeightRatio <= ThinVerticalMaxWidthToHeightRatio;
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
