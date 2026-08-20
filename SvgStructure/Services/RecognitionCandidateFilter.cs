using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Shared law for semantic passes after meter detection. Earlier recognized geometry suppresses a
/// later candidate only when both bounding boxes are practically the same. A large recognized object
/// (for example an arc) must not hide unrelated primitives merely because they lie inside its bbox.
/// </summary>
public static class RecognitionCandidateFilter
{
    public const double MaxClaimedEdgeGapFraction = 0.05;

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
        claimedBounds.Any(x => NearlySameBounds(x, candidate));

    /// <summary>
    /// The candidate must be contained by the recognized bbox and repeat all four edges within 5%
    /// of that bbox's width/height. This intentionally rejects the old broad "anything inside" rule.
    /// </summary>
    public static bool NearlySameBounds(
        RectD recognized,
        RectD candidate,
        double maxEdgeGapFraction = MaxClaimedEdgeGapFraction)
    {
        if (!Contains(recognized, candidate))
            return false;

        if (recognized.Width <= 1e-9 || recognized.Height <= 1e-9)
            return false;

        var maxXGap = recognized.Width * maxEdgeGapFraction;
        var maxYGap = recognized.Height * maxEdgeGapFraction;

        return candidate.Left - recognized.Left <= maxXGap &&
               recognized.Right - candidate.Right <= maxXGap &&
               candidate.Top - recognized.Top <= maxYGap &&
               recognized.Bottom - candidate.Bottom <= maxYGap;
    }

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
