using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Resolves standalone note flags. A flag is considered only next to the free endpoint of a stem.
/// Any stem touched by a recognized beam is not free and therefore cannot own a standalone flag.
/// </summary>
public sealed class NoteFlagResolver
{
    private readonly GlyphPcaNoteFlagRecognizer _recognizer;
    private readonly double _maxEndpointDistanceInStaffSpaces;

    public NoteFlagResolver(
        GlyphPcaNoteFlagRecognizer recognizer,
        double maxEndpointDistanceInStaffSpaces = 1.0)
    {
        _recognizer = recognizer;
        _maxEndpointDistanceInStaffSpaces = maxEndpointDistanceInStaffSpaces;
    }

    public IReadOnlyList<NoteFlagResolution> Resolve(
        MusicSymbolResolution symbols,
        LogicalGridResolution grid,
        IReadOnlyList<StemResolution> stems,
        IReadOnlyList<BeamResolution> beams)
    {
        var beamedStems = beams
            .SelectMany(x => x.Stems)
            .ToHashSet();

        var freeStems = stems
            .Where(x => !beamedStems.Contains(x))
            .ToArray();

        if (freeStems.Length == 0)
            return Array.Empty<NoteFlagResolution>();

        var hits = new List<Hit>();

        foreach (var stem in freeStems)
        {
            if (!grid.TryGetBlock(stem.PartNumber, stem.MeasureNumber, out var block))
                continue;

            var staffSpace = block.PhysicalBounds.Height / 4.0;
            var maxDistance = staffSpace * _maxEndpointDistanceInStaffSpaces;
            var endpoint = FreeEndpoint(stem);

            var nearbySymbols = symbols.Candidates
                .Where(x => x.MeasureNumber == stem.MeasureNumber)
                .Where(x => x.PartNumber is null || x.PartNumber == stem.PartNumber)
                .Where(x => x.SmoothPaths.Count > 0)
                .Select(x => new
                {
                    Symbol = x,
                    Distance = Distance(endpoint, x.PhysicalBounds)
                })
                .Where(x => x.Distance <= maxDistance)
                .OrderBy(x => x.Distance)
                .ToArray();

            foreach (var item in nearbySymbols)
            {
                var contours = SmoothSymbolContourConverter.ToContours(new[] { item.Symbol });
                if (contours.Count == 0)
                    continue;

                var recognition = _recognizer.Recognize(contours);
                if (recognition.Denominator is null || recognition.Direction is null)
                    continue;

                // SMuFL has separate up/down flag glyphs. Requiring the same direction is a useful
                // sanity check and prevents a nearby unrelated curl from attaching to the stem.
                if (recognition.Direction.Value != stem.Direction)
                    continue;

                var logicalBounds = block.ToLogical(item.Symbol.PhysicalBounds);
                hits.Add(new Hit(
                    stem,
                    new NoteFlagResolution(
                        stem.PartNumber,
                        stem.MeasureNumber,
                        recognition.Denominator.Value,
                        item.Symbol.PhysicalBounds,
                        logicalBounds,
                        stem,
                        recognition.Confidence),
                    item.Distance));
            }
        }

        // One standalone flag glyph per stem: the PCA class itself encodes 1/8, 1/16 or 1/32.
        return hits
            .GroupBy(x => x.Stem)
            .Select(group => group
                .OrderByDescending(x => x.Flag.Confidence)
                .ThenBy(x => x.EndpointDistance)
                .First()
                .Flag)
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ToArray();
    }

    private static PointD FreeEndpoint(StemResolution stem) =>
        stem.Direction == StemDirection.Up
            ? new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Top)
            : new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Bottom);

    private static double Distance(PointD point, RectD rect)
    {
        var dx = point.X < rect.Left ? rect.Left - point.X
            : point.X > rect.Right ? point.X - rect.Right
            : 0;
        var dy = point.Y < rect.Top ? rect.Top - point.Y
            : point.Y > rect.Bottom ? point.Y - rect.Bottom
            : 0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed record Hit(
        StemResolution Stem,
        NoteFlagResolution Flag,
        double EndpointDistance);
}
