using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Evaluates only direct geometric overlap between a raw primitive and staff-measure regions.
/// A mere touch of a border is not an assignment. A clearly dominant overlap becomes a hard
/// anchor; two genuinely comparable overlaps remain ambiguous and are intentionally kept gray.
/// </summary>
public sealed class DirectPrimitiveAssigner
{
    public double MinOverlapRatio { get; init; } = 0.08;
    public double AmbiguousRatioToBest { get; init; } = 0.70;

    public DirectPrimitiveAssignment Assign(
        RawPrimitive primitive,
        IReadOnlyList<StaffMeasureRegion> regions)
    {
        var scores = regions
            .Select(region => new DirectOverlapScore(
                region.Key,
                GetOverlapRatio(primitive.Bounds, region.Bounds)))
            .Where(x => x.OverlapRatio >= MinOverlapRatio)
            .OrderByDescending(x => x.OverlapRatio)
            .ToList();

        if (scores.Count == 0)
            return new DirectPrimitiveAssignment(primitive.Id, Array.Empty<StaffMeasureKey>(), scores);

        var best = scores[0].OverlapRatio;
        var winners = scores
            .Where(x => x.OverlapRatio >= best * AmbiguousRatioToBest)
            .Select(x => x.Key)
            .Distinct()
            .ToArray();

        return new DirectPrimitiveAssignment(primitive.Id, winners, scores);
    }

    private static double GetOverlapRatio(RectD primitive, RectD region)
    {
        var intersectionWidth = Math.Max(0, Math.Min(primitive.Right, region.Right) - Math.Max(primitive.Left, region.Left));
        var intersectionHeight = Math.Max(0, Math.Min(primitive.Bottom, region.Bottom) - Math.Max(primitive.Top, region.Top));

        // Touching a border has zero width/height and therefore must not become a hard anchor.
        if (intersectionWidth <= 0 || intersectionHeight <= 0)
            return 0;

        var area = primitive.Width * primitive.Height;
        if (area > 0.0001)
            return intersectionWidth * intersectionHeight / area;

        // Defensive fallback for degenerate line-like bounds.
        if (primitive.Width >= primitive.Height && primitive.Width > 0)
            return intersectionWidth / primitive.Width;

        if (primitive.Height > 0)
            return intersectionHeight / primitive.Height;

        return 0;
    }
}

public sealed record DirectPrimitiveAssignment(
    int PrimitiveId,
    IReadOnlyList<StaffMeasureKey> Keys,
    IReadOnlyList<DirectOverlapScore> Scores)
{
    public bool IsUnassigned => Keys.Count == 0;
    public bool IsHardAnchor => Keys.Count == 1;
    public bool IsAmbiguous => Keys.Count > 1;
}

public sealed record DirectOverlapScore(
    StaffMeasureKey Key,
    double OverlapRatio);
