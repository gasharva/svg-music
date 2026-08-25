using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed record RestDiagnosticEntry(
    int PartNumber,
    int MeasureNumber,
    MusicSymbolCandidate Candidate,
    LogicalRectD LogicalBounds,
    bool OnLegalStaffPosition,
    bool PreviouslyRecognized,
    RestRecognition? Recognition,
    string Verdict);

public sealed class RestResolver
{
    private readonly GeometryRestRecognizer _recognizer;
    private readonly List<RestDiagnosticEntry> _diagnostics = new();

    public double MaxDistanceInStaffSpaces { get; init; } = 2.0;

    public RestResolver(GeometryRestRecognizer recognizer) => _recognizer = recognizer;

    public IReadOnlyList<RestDiagnosticEntry> LastDiagnostics => _diagnostics;

    public IReadOnlyList<RestResolution> Resolve(
        MusicSymbolResolution symbols,
        LogicalGridResolution grid,
        IReadOnlyList<RectD> previouslyRecognizedBounds)
    {
        _diagnostics.Clear();
        var results = new List<RestResolution>();
        var candidates = symbols.Candidates.Where(x => x.MeasureNumber > 0).Where(x => x.SmoothPaths.Count > 0)
            .OrderBy(x => x.MeasureNumber).ThenBy(x => x.PartNumber).ThenBy(x => x.PhysicalBounds.Left).ToArray();

        foreach (var candidate in candidates)
        {
            var block = ResolveBlock(candidate, grid);
            if (block is null) continue;
            var partNumber = block.PartNumber;
            var logical = block.ToLogical(candidate.PhysicalBounds);
            if (RecognitionCandidateFilter.IsClaimed(candidate.PhysicalBounds, previouslyRecognizedBounds))
            {
                _diagnostics.Add(new(partNumber,candidate.MeasureNumber,candidate,logical,true,true,null,"skipped: contained in previously recognized object"));
                continue;
            }
            if (!IsNearScore(candidate, grid, block, previouslyRecognizedBounds))
            {
                _diagnostics.Add(new(partNumber,candidate.MeasureNumber,candidate,logical,false,false,null,$"rejected before glyph recognition: farther than {MaxDistanceInStaffSpaces:0.##} staff spaces from staff/recognized object"));
                continue;
            }
            var contours = SmoothSymbolContourConverter.ToContours(new[] { candidate });
            if (contours.Count == 0)
            {
                _diagnostics.Add(new(partNumber,candidate.MeasureNumber,candidate,logical,true,false,null,"rejected: no usable contours"));
                continue;
            }
            var recognition = _recognizer.Recognize(contours);
            if (recognition.Denominator is null)
            {
                _diagnostics.Add(new(partNumber,candidate.MeasureNumber,candidate,logical,true,false,recognition,"rejected by geometry classifier"));
                continue;
            }
            results.Add(new RestResolution(partNumber,candidate.MeasureNumber,recognition.Denominator.Value,logical,candidate.PhysicalBounds,recognition.Confidence,candidate.Id));
            _diagnostics.Add(new(partNumber,candidate.MeasureNumber,candidate,logical,true,false,recognition,$"accepted: 1/{recognition.Denominator.Value}"));
        }
        return results.OrderBy(x => x.MeasureNumber).ThenBy(x => x.PartNumber).ThenBy(x => x.LogicalBounds.Left ?? double.MinValue).ThenBy(x => x.LogicalBounds.Top).ToArray();
    }

    private bool IsNearScore(MusicSymbolCandidate candidate, LogicalGridResolution grid, LogicalGridBlock nearestBlock, IReadOnlyList<RectD> previouslyRecognizedBounds)
    {
        var staffSpace = Math.Max(1e-9, nearestBlock.PhysicalBounds.Height / 4.0);
        var maxDistance = MaxDistanceInStaffSpaces * staffSpace;
        var sameMeasureStaffs = grid.Blocks.Where(x => x.MeasureNumber == candidate.MeasureNumber).Select(x => x.PhysicalBounds).ToArray();
        var distanceToStaff = sameMeasureStaffs.Length == 0 ? double.PositiveInfinity : sameMeasureStaffs.Min(x => RecognitionCandidateFilter.Distance(candidate.PhysicalBounds, x));
        if (distanceToStaff <= maxDistance) return true;
        var distanceToRecognized = previouslyRecognizedBounds.Count == 0 ? double.PositiveInfinity : previouslyRecognizedBounds.Min(x => RecognitionCandidateFilter.Distance(candidate.PhysicalBounds, x));
        return distanceToRecognized <= maxDistance;
    }

    private static LogicalGridBlock? ResolveBlock(MusicSymbolCandidate candidate, LogicalGridResolution grid)
    {
        if (candidate.PartNumber is { } partNumber && grid.TryGetBlock(partNumber, candidate.MeasureNumber, out var exact)) return exact;
        var sameMeasure = grid.Blocks.Where(x => x.MeasureNumber == candidate.MeasureNumber).ToArray();
        if (sameMeasure.Length == 0) return null;
        var centerY = candidate.PhysicalBounds.CenterY;
        return sameMeasure.OrderBy(x => VerticalDistance(centerY, x.PhysicalBounds)).ThenBy(x => Math.Abs(centerY - x.PhysicalBounds.CenterY)).First();
    }
    private static double VerticalDistance(double y, RectD rect) => y < rect.Top ? rect.Top - y : y > rect.Bottom ? y - rect.Bottom : 0;
}
