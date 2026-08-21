using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Removes very thin vertically-wavy symbols from meter recognition only.
/// These are typical arpeggiato fragments: they must stay available to later primitive resolvers,
/// but should never be offered to the numeric meter classifier.
/// </summary>
public static class ThinWavySymbolFilter
{
    public static MusicSymbolResolution ExcludeForMeter(
        MusicSymbolResolution symbols,
        PartMeasureBlock block)
    {
        var staffHeight = Math.Max(1e-9, block.PhysicalBounds.Height);
        var primitiveById = symbols.Primitives.Primitives.ToDictionary(x => x.Id);

        var filtered = symbols.Candidates
            .Where(candidate => !LooksLikeThinWave(candidate, block, staffHeight, primitiveById))
            .ToArray();

        return new MusicSymbolResolution(symbols.Primitives, filtered);
    }

    private static bool LooksLikeThinWave(
        MusicSymbolCandidate candidate,
        PartMeasureBlock block,
        double staffHeight,
        IReadOnlyDictionary<int, ResolvedPrimitive> primitiveById)
    {
        if (candidate.MeasureNumber != block.MeasureNumber)
            return false;
        if (candidate.PartNumber is { } part && part != block.PartNumber)
            return false;

        var bounds = candidate.PhysicalBounds;
        if (bounds.Height < staffHeight * 0.20 || bounds.Height > staffHeight * 0.80)
            return false;
        if (bounds.Width <= 1e-9 || bounds.Height <= 1e-9)
            return false;

        // Real meter digits are substantially chunkier. Keep the geometric test deliberately
        // conservative, then require repeated horizontal oscillation so a narrow digit "1" survives.
        if (bounds.Width / bounds.Height > 0.36)
            return false;

        var contours = candidate.PrimitiveIds
            .Where(primitiveById.ContainsKey)
            .Select(id => primitiveById[id].Contour)
            .Where(x => x.Points.Count >= 8)
            .ToArray();

        return contours.Any(contour => HasRepeatedHorizontalOscillation(contour, bounds));
    }

    private static bool HasRepeatedHorizontalOscillation(PrimitiveContour contour, RectD bounds)
    {
        const int sliceCount = 14;
        var sliceHeight = bounds.Height / sliceCount;
        if (sliceHeight <= 1e-9)
            return false;

        var centers = new List<double>();
        for (var i = 0; i < sliceCount; i++)
        {
            var top = bounds.Top + i * sliceHeight;
            var bottom = i == sliceCount - 1 ? bounds.Bottom + 1e-9 : top + sliceHeight;
            var xs = contour.Points
                .Where(p => p.Y >= top && p.Y < bottom)
                .Select(p => (double)p.X)
                .ToArray();
            if (xs.Length > 0)
                centers.Add((xs.Min() + xs.Max()) / 2.0);
        }

        if (centers.Count < 7)
            return false;

        var range = centers.Max() - centers.Min();
        if (range < bounds.Width * 0.25)
            return false;

        var deadBand = Math.Max(1e-9, range * 0.08);
        var changes = 0;
        var lastSign = 0;
        for (var i = 1; i < centers.Count; i++)
        {
            var dx = centers[i] - centers[i - 1];
            var sign = dx > deadBand ? 1 : dx < -deadBand ? -1 : 0;
            if (sign == 0)
                continue;
            if (lastSign != 0 && sign != lastSign)
                changes++;
            lastSign = sign;
        }

        return changes >= 2;
    }
}
