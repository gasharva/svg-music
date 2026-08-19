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

/// <summary>
/// Resolves rests from still-unclaimed MusicSymbol candidates. Positional legality intentionally
/// mirrors NoteHeadResolver: on/in the staff, first outside half-space, or farther out only when the
/// candidate sits over a recognized ledger ladder.
/// </summary>
public sealed class RestResolver
{
    private readonly GlyphPcaRestRecognizer _recognizer;
    private readonly List<RestDiagnosticEntry> _diagnostics = new();

    public RestResolver(GlyphPcaRestRecognizer recognizer)
    {
        _recognizer = recognizer;
    }

    public IReadOnlyList<RestDiagnosticEntry> LastDiagnostics => _diagnostics;

    public IReadOnlyList<RestResolution> Resolve(
        MusicSymbolResolution symbols,
        LogicalGridResolution grid,
        IReadOnlyList<LedgerLineResolution> ledgerLines,
        IReadOnlyList<MeterResolution> meters,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<AccidentalResolution> accidentals,
        IReadOnlyList<StemResolution> stems,
        IReadOnlyList<BeamResolution> beams,
        IReadOnlyList<NoteFlagResolution> noteFlags,
        IReadOnlyList<ArcResolution> arcs)
    {
        _diagnostics.Clear();
        var results = new List<RestResolution>();

        var candidates = symbols.Candidates
            .Where(x => x.PartNumber is not null)
            .Where(x => x.SmoothPaths.Count > 0)
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var partNumber = candidate.PartNumber!.Value;
            if (!grid.TryGetBlock(partNumber, candidate.MeasureNumber, out var block))
                continue;

            var logical = block.ToLogical(candidate.PhysicalBounds);
            var previouslyRecognized = IsPreviouslyRecognized(
                candidate.PhysicalBounds,
                candidate.MeasureNumber,
                partNumber,
                meters,
                clefs,
                noteHeads,
                accidentals,
                stems,
                beams,
                noteFlags,
                arcs);

            if (previouslyRecognized)
            {
                _diagnostics.Add(new RestDiagnosticEntry(
                    partNumber,
                    candidate.MeasureNumber,
                    candidate,
                    logical,
                    true,
                    true,
                    null,
                    "skipped: overlaps previously recognized symbol"));
                continue;
            }

            var onLegalStaffPosition = IsOnLegalStaffPosition(
                partNumber,
                candidate.MeasureNumber,
                logical,
                ledgerLines);
            if (!onLegalStaffPosition)
            {
                _diagnostics.Add(new RestDiagnosticEntry(
                    partNumber,
                    candidate.MeasureNumber,
                    candidate,
                    logical,
                    false,
                    false,
                    null,
                    "rejected before PCA: not on staff/ledger position"));
                continue;
            }

            var contours = SmoothSymbolContourConverter.ToContours(new[] { candidate });
            if (contours.Count == 0)
            {
                _diagnostics.Add(new RestDiagnosticEntry(
                    partNumber,
                    candidate.MeasureNumber,
                    candidate,
                    logical,
                    true,
                    false,
                    null,
                    "rejected: no usable contours"));
                continue;
            }

            var recognition = _recognizer.Recognize(contours);
            if (recognition.Denominator is null)
            {
                _diagnostics.Add(new RestDiagnosticEntry(
                    partNumber,
                    candidate.MeasureNumber,
                    candidate,
                    logical,
                    true,
                    false,
                    recognition,
                    "rejected by PCA"));
                continue;
            }

            var rest = new RestResolution(
                partNumber,
                candidate.MeasureNumber,
                recognition.Denominator.Value,
                logical,
                candidate.PhysicalBounds,
                recognition.Confidence,
                candidate.Id);
            results.Add(rest);

            _diagnostics.Add(new RestDiagnosticEntry(
                partNumber,
                candidate.MeasureNumber,
                candidate,
                logical,
                true,
                false,
                recognition,
                $"accepted: 1/{recognition.Denominator.Value}"));
        }

        return results
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.LogicalBounds.Left ?? double.MinValue)
            .ThenBy(x => x.LogicalBounds.Top)
            .ToArray();
    }

    private static bool IsOnLegalStaffPosition(
        int partNumber,
        int measureNumber,
        LogicalRectD logicalBounds,
        IReadOnlyList<LedgerLineResolution> ledgerLines)
    {
        var centerY = (logicalBounds.Top + logicalBounds.Bottom) / 2.0;
        var position = (int)Math.Round(centerY);

        if (position >= 0 && position <= 8)
            return true;
        if (position is -1 or 9)
            return true;

        var requiredDepth = position < -1
            ? (int)Math.Ceiling((Math.Abs(position) - 1) / 2.0)
            : (int)Math.Ceiling((position - 9) / 2.0);

        var centerX = LogicalCenterX(logicalBounds);
        if (centerX is null)
            return false;

        var matchingLadders = ledgerLines
            .Where(x => x.PartNumber == partNumber && x.MeasureNumber == measureNumber)
            .Where(x => Math.Sign(x.Depth) == (position < 0 ? -1 : 1))
            .Where(x => Math.Abs(x.Depth) >= requiredDepth)
            .ToArray();

        return matchingLadders.Any(x => ContainsLogicalX(x.LogicalBounds, centerX.Value));
    }

    private static bool IsPreviouslyRecognized(
        RectD candidate,
        int measureNumber,
        int partNumber,
        IReadOnlyList<MeterResolution> meters,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<AccidentalResolution> accidentals,
        IReadOnlyList<StemResolution> stems,
        IReadOnlyList<BeamResolution> beams,
        IReadOnlyList<NoteFlagResolution> noteFlags,
        IReadOnlyList<ArcResolution> arcs)
    {
        var occupied = new List<RectD>();

        occupied.AddRange(meters
            .Where(x => x.MeasureNumber == measureNumber && x.PartNumber == partNumber)
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(clefs
            .Where(x => x.MeasureNumber == measureNumber && x.PartNumber == partNumber)
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(noteHeads
            .Where(x => x.MeasureNumber == measureNumber && x.PartNumber == partNumber)
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(accidentals
            .Where(x => x.MeasureNumber == measureNumber && x.PartNumber == partNumber)
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(stems
            .Where(x => x.MeasureNumber == measureNumber && x.PartNumber == partNumber)
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(noteFlags
            .Where(x => x.MeasureNumber == measureNumber && x.PartNumber == partNumber)
            .Select(x => x.PhysicalBounds));

        occupied.AddRange(beams
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => x.Stems.Any(s => s.PartNumber == partNumber))
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(arcs
            .Where(x => x.MeasureNumber == measureNumber)
            .Select(x => x.PhysicalBounds));

        return occupied.Any(x => SignificantOverlap(candidate, x));
    }

    private static bool SignificantOverlap(RectD a, RectD b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top)
            return false;

        var overlap = (right - left) * (bottom - top);
        var candidateArea = Math.Max(1e-9, a.Width * a.Height);
        return overlap / candidateArea >= 0.55;
    }

    private static bool ContainsLogicalX(LogicalRectD bounds, double x) =>
        bounds.Left is { } left && bounds.Right is { } right && x >= left && x <= right;

    private static double? LogicalCenterX(LogicalRectD bounds) =>
        bounds.Left is { } left && bounds.Right is { } right ? (left + right) / 2.0 : null;
}
