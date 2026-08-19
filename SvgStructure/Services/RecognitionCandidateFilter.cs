using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Shared law for all semantic passes after meter detection: geometry already claimed by an earlier
/// pass is no longer a candidate. A later candidate is removed when its physical bbox is fully inside
/// (including equality with) a previously recognized bbox.
/// </summary>
public static class RecognitionCandidateFilter
{
    public static MusicSymbolResolution ExcludeClaimed(
        MusicSymbolResolution symbols,
        IEnumerable<RectD> claimedBounds)
    {
        var claimed = claimedBounds.ToArray();
        if (claimed.Length == 0)
            return symbols;

        var candidates = symbols.Candidates
            .Where(x => !IsClaimed(x.PhysicalBounds, claimed))
            .ToArray();

        return new MusicSymbolResolution(symbols.Primitives, candidates);
    }

    public static PrimitiveResolution ExcludeClaimed(
        PrimitiveResolution primitives,
        IEnumerable<RectD> claimedBounds)
    {
        var claimed = claimedBounds.ToArray();
        if (claimed.Length == 0)
            return primitives;

        var remaining = primitives.Primitives
            .Where(x => !IsClaimed(x.PhysicalBounds, claimed))
            .ToArray();

        return new PrimitiveResolution(primitives.Structure, remaining);
    }

    public static bool IsClaimed(RectD candidate, IEnumerable<RectD> claimedBounds) =>
        claimedBounds.Any(x => Contains(x, candidate));

    public static bool Contains(RectD outer, RectD inner) =>
        inner.Left >= outer.Left &&
        inner.Top >= outer.Top &&
        inner.Right <= outer.Right &&
        inner.Bottom <= outer.Bottom;

    /// <summary>Shortest Euclidean gap between two rectangles; zero when they touch or overlap.</summary>
    public static double Distance(RectD a, RectD b)
    {
        var dx = a.Right < b.Left ? b.Left - a.Right
            : b.Right < a.Left ? a.Left - b.Right
            : 0.0;
        var dy = a.Bottom < b.Top ? b.Top - a.Bottom
            : b.Bottom < a.Top ? a.Top - b.Bottom
            : 0.0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
