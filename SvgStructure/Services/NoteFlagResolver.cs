using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed record NoteFlagDiagnosticEntry(
    int PartNumber,
    int MeasureNumber,
    StemResolution Stem,
    MusicSymbolCandidate Candidate,
    double EndpointDistanceInStaffSpaces,
    bool PassedGeometrySanity,
    NoteFlagRecognition? Recognition,
    string Verdict);

/// <summary>
/// Resolves standalone note flags. A flag is considered only next to the free endpoint of a stem.
/// Any stem touched by a recognized beam is not free and therefore cannot own a standalone flag.
/// </summary>
public sealed class NoteFlagResolver
{
    private readonly GlyphPcaNoteFlagRecognizer _recognizer;
    private readonly double _maxEndpointDistanceInStaffSpaces;
    private readonly List<NoteFlagDiagnosticEntry> _diagnostics = new();

    public NoteFlagResolver(
        GlyphPcaNoteFlagRecognizer recognizer,
        double maxEndpointDistanceInStaffSpaces = 1.25)
    {
        _recognizer = recognizer;
        _maxEndpointDistanceInStaffSpaces = maxEndpointDistanceInStaffSpaces;
    }

    public IReadOnlyList<NoteFlagDiagnosticEntry> LastDiagnostics => _diagnostics;

    public IReadOnlyList<NoteFlagResolution> Resolve(
        MusicSymbolResolution symbols,
        LogicalGridResolution grid,
        IReadOnlyList<StemResolution> stems,
        IReadOnlyList<BeamResolution> beams)
    {
        _diagnostics.Clear();

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
                var distanceInStaffSpaces = item.Distance / Math.Max(1e-9, staffSpace);

                if (!PassesFlagGeometrySanity(item.Symbol.PhysicalBounds, stem, endpoint, staffSpace, out var geometryReason))
                {
                    _diagnostics.Add(new NoteFlagDiagnosticEntry(
                        stem.PartNumber,
                        stem.MeasureNumber,
                        stem,
                        item.Symbol,
                        distanceInStaffSpaces,
                        false,
                        null,
                        "rejected before PCA: " + geometryReason));
                    continue;
                }

                var contours = SmoothSymbolContourConverter.ToContours(new[] { item.Symbol });
                if (contours.Count == 0)
                {
                    _diagnostics.Add(new NoteFlagDiagnosticEntry(
                        stem.PartNumber,
                        stem.MeasureNumber,
                        stem,
                        item.Symbol,
                        distanceInStaffSpaces,
                        true,
                        null,
                        "rejected: no usable contours"));
                    continue;
                }

                var recognition = _recognizer.Recognize(contours);
                if (recognition.Denominator is null || recognition.Direction is null)
                {
                    _diagnostics.Add(new NoteFlagDiagnosticEntry(
                        stem.PartNumber,
                        stem.MeasureNumber,
                        stem,
                        item.Symbol,
                        distanceInStaffSpaces,
                        true,
                        recognition,
                        "rejected by PCA"));
                    continue;
                }

                if (recognition.Direction.Value != stem.Direction)
                {
                    _diagnostics.Add(new NoteFlagDiagnosticEntry(
                        stem.PartNumber,
                        stem.MeasureNumber,
                        stem,
                        item.Symbol,
                        distanceInStaffSpaces,
                        true,
                        recognition,
                        $"rejected: PCA direction {recognition.Direction.Value} != stem {stem.Direction}"));
                    continue;
                }

                var logicalBounds = block.ToLogical(item.Symbol.PhysicalBounds);
                var flag = new NoteFlagResolution(
                    stem.PartNumber,
                    stem.MeasureNumber,
                    recognition.Denominator.Value,
                    item.Symbol.PhysicalBounds,
                    logicalBounds,
                    stem,
                    recognition.Confidence);

                hits.Add(new Hit(stem, flag, item.Distance));
                _diagnostics.Add(new NoteFlagDiagnosticEntry(
                    stem.PartNumber,
                    stem.MeasureNumber,
                    stem,
                    item.Symbol,
                    distanceInStaffSpaces,
                    true,
                    recognition,
                    $"accepted: 1/{recognition.Denominator.Value} {recognition.Direction.Value}"));
            }
        }

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

    private static bool PassesFlagGeometrySanity(
        RectD candidate,
        StemResolution stem,
        PointD freeEndpoint,
        double staffSpace,
        out string reason)
    {
        var minWidth = Math.Max(stem.PhysicalBounds.Width * 2.0, staffSpace * 0.16);
        if (candidate.Width < minWidth)
        {
            reason = $"width {candidate.Width / staffSpace:0.###}sp < {minWidth / staffSpace:0.###}sp";
            return false;
        }

        var minHeight = staffSpace * 0.28;
        if (candidate.Height < minHeight)
        {
            reason = $"height {candidate.Height / staffSpace:0.###}sp < {minHeight / staffSpace:0.###}sp";
            return false;
        }

        var attachedEndpointY = stem.Direction == StemDirection.Up
            ? stem.PhysicalBounds.Bottom
            : stem.PhysicalBounds.Top;
        var freeDistance = VerticalDistance(freeEndpoint.Y, candidate);
        var attachedDistance = VerticalDistance(attachedEndpointY, candidate);
        if (attachedDistance + staffSpace * 0.05 < freeDistance)
        {
            reason = $"closer to note endpoint ({attachedDistance / staffSpace:0.###}sp) than free endpoint ({freeDistance / staffSpace:0.###}sp)";
            return false;
        }

        reason = "ok";
        return true;
    }

    private static double VerticalDistance(double y, RectD rect) =>
        y < rect.Top ? rect.Top - y
        : y > rect.Bottom ? y - rect.Bottom
        : 0;

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
