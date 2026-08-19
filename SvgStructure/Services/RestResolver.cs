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
/// Resolves rests from still-unclaimed MusicSymbol candidates. Rests are deliberately allowed at any
/// vertical position: unlike note heads, their placement is not constrained to staff/ledger steps.
/// A candidate only needs a measure; when its source part is unknown we attach it to the vertically
/// nearest logical block of that measure for downstream coordinates.
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
            .Where(x => x.MeasureNumber > 0)
            .Where(x => x.SmoothPaths.Count > 0)
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var block = ResolveBlock(candidate, grid);
            if (block is null)
                continue;

            var partNumber = block.PartNumber;
            var logical = block.ToLogical(candidate.PhysicalBounds);
            var previouslyRecognized = IsPreviouslyRecognized(
                candidate.PhysicalBounds,
                candidate.MeasureNumber,
                candidate.PartNumber,
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

    private static LogicalGridBlock? ResolveBlock(
        MusicSymbolCandidate candidate,
        LogicalGridResolution grid)
    {
        if (candidate.PartNumber is { } partNumber &&
            grid.TryGetBlock(partNumber, candidate.MeasureNumber, out var exact))
            return exact;

        var sameMeasure = grid.Blocks
            .Where(x => x.MeasureNumber == candidate.MeasureNumber)
            .ToArray();
        if (sameMeasure.Length == 0)
            return null;

        var centerY = candidate.PhysicalBounds.CenterY;
        return sameMeasure
            .OrderBy(x => VerticalDistance(centerY, x.PhysicalBounds))
            .ThenBy(x => Math.Abs(centerY - x.PhysicalBounds.CenterY))
            .First();
    }

    private static bool IsPreviouslyRecognized(
        RectD candidate,
        int measureNumber,
        int? sourcePartNumber,
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

        bool SamePart(int partNumber) =>
            sourcePartNumber is null || partNumber == sourcePartNumber.Value;

        occupied.AddRange(meters
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => SamePart(x.PartNumber))
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(clefs
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => SamePart(x.PartNumber))
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(noteHeads
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => SamePart(x.PartNumber))
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(accidentals
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => SamePart(x.PartNumber))
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(stems
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => SamePart(x.PartNumber))
            .Select(x => x.PhysicalBounds));
        occupied.AddRange(noteFlags
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => SamePart(x.PartNumber))
            .Select(x => x.PhysicalBounds));

        occupied.AddRange(beams
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => sourcePartNumber is null || x.Stems.Any(s => s.PartNumber == sourcePartNumber.Value))
            .Select(x => x.PhysicalBounds));

        occupied.AddRange(arcs
            .Where(x => ArcTouchesMeasureAndOptionalPart(x, sourcePartNumber, measureNumber))
            .Select(x => x.PhysicalBounds));

        return occupied.Any(x => SignificantOverlap(candidate, x));
    }

    private static bool ArcTouchesMeasureAndOptionalPart(
        ArcResolution arc,
        int? partNumber,
        int measureNumber)
    {
        var notes = arc.Notes.Where(x => x.MeasureNumber == measureNumber);
        var stems = arc.Stems.Where(x => x.MeasureNumber == measureNumber);

        if (partNumber is null)
            return notes.Any() || stems.Any();

        return notes.Any(x => x.PartNumber == partNumber.Value) ||
               stems.Any(x => x.PartNumber == partNumber.Value);
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

    private static double VerticalDistance(double y, RectD rect) =>
        y < rect.Top ? rect.Top - y
        : y > rect.Bottom ? y - rect.Bottom
        : 0;
}