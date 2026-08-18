using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Resolves accidentals from smooth MusicSymbol candidates. PCA answers only the glyph question;
/// placement rules decide whether the recognized glyph is musically plausible and which note owns it.
/// </summary>
public sealed class AccidentalResolver
{
    private readonly GlyphPcaAccidentalRecognizer _recognizer;

    public double MinimumConfidence { get; init; } = 0.70;
    public double MaxNoteGapLogicalX { get; init; } = 2.0;
    public double MinLogicalHeight { get; init; } = 1.5;
    public double MaxLogicalHeight { get; init; } = 7.0;

    public AccidentalResolver(GlyphPcaAccidentalRecognizer recognizer) => _recognizer = recognizer;

    public IReadOnlyList<AccidentalResolution> Resolve(
        MusicSymbolResolution symbols,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<MeterResolution> meters)
    {
        var result = new List<AccidentalResolution>();

        var candidates = symbols.Candidates
            .Where(x => x.Scope == PrimitiveLogicalScope.PartMeasure && x.PartNumber is not null)
            .Where(x => x.SmoothPaths.Count > 0)
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ToArray();

        foreach (var symbol in candidates)
        {
            var part = symbol.PartNumber!.Value;
            if (!grid.TryGetBlock(part, symbol.MeasureNumber, out var block))
                continue;

            var logical = block.ToLogical(symbol.PhysicalBounds);
            var logicalHeight = logical.Bottom - logical.Top;
            if (logicalHeight < MinLogicalHeight || logicalHeight > MaxLogicalHeight)
                continue;

            var contours = SmoothSymbolContourConverter.ToContours(new[] { symbol });
            if (contours.Count == 0)
                continue;

            var recognition = _recognizer.Recognize(contours);
            if (recognition.Kind is null || recognition.Confidence < MinimumConfidence)
                continue;

            var centerX = CenterX(logical);
            if (centerX is null)
                continue;

            var keyPrefix = IsKeyPrefixCandidate(
                part,
                symbol.MeasureNumber,
                centerX.Value,
                logical,
                noteHeads,
                clefs,
                meters,
                result,
                block);

            NoteHeadResolution? note = null;
            if (!keyPrefix)
            {
                note = FindAttachedNote(
                    part,
                    symbol.MeasureNumber,
                    centerX.Value,
                    noteHeads,
                    clefs,
                    meters,
                    result,
                    block);
                if (note is null)
                    continue;
            }

            result.Add(new AccidentalResolution(
                part,
                symbol.MeasureNumber,
                logical,
                symbol.PhysicalBounds,
                recognition.Kind.Value,
                recognition.Confidence,
                note));
        }

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => CenterX(x.LogicalBounds) ?? double.MaxValue)
            .ToArray();
    }

    private static bool IsKeyPrefixCandidate(
        int part,
        int measure,
        double x,
        LogicalRectD bounds,
        IReadOnlyList<NoteHeadResolution> notes,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<MeterResolution> meters,
        IReadOnlyList<AccidentalResolution> alreadyResolved,
        LogicalGridBlock block)
    {
        // Key-signature accidental must sit on/very close to the staff. y=-1 permits the G position
        // immediately above the top staff line, as requested.
        var cy = (bounds.Top + bounds.Bottom) / 2.0;
        if (cy < -1.25 || cy > 8.75)
            return false;

        var blockersToLeft = new List<double>();
        blockersToLeft.AddRange(notes
            .Where(n => n.PartNumber == part && n.MeasureNumber == measure)
            .Select(n => CenterX(n.LogicalBounds))
            .Where(v => v.HasValue)
            .Select(v => v!.Value));
        blockersToLeft.AddRange(meters
            .Where(m => m.PartNumber == part && m.MeasureNumber == measure)
            .Select(m => CenterX(block.ToLogical(m.PhysicalBounds)))
            .Where(v => v.HasValue)
            .Select(v => v!.Value));
        blockersToLeft.AddRange(alreadyResolved
            .Where(a => a.PartNumber == part && a.MeasureNumber == measure && a.Note is not null)
            .Select(a => CenterX(a.LogicalBounds))
            .Where(v => v.HasValue)
            .Select(v => v!.Value));

        // Clefs are explicitly allowed to the left. Everything else already understood as rhythm is not.
        return blockersToLeft.All(b => b >= x);
    }

    private NoteHeadResolution? FindAttachedNote(
        int part,
        int measure,
        double accidentalX,
        IReadOnlyList<NoteHeadResolution> notes,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<MeterResolution> meters,
        IReadOnlyList<AccidentalResolution> alreadyResolved,
        LogicalGridBlock block)
    {
        var rightNotes = notes
            .Where(n => n.PartNumber == part && n.MeasureNumber == measure)
            .Select(n => new { Note = n, X = CenterX(n.LogicalBounds) })
            .Where(x => x.X is not null && x.X.Value > accidentalX)
            .OrderBy(x => x.X)
            .ToArray();

        var nearest = rightNotes.FirstOrDefault();
        if (nearest is null || nearest.X!.Value - accidentalX > MaxNoteGapLogicalX)
            return null;

        var targetX = nearest.X.Value;
        var blockerXs = new List<double>();
        blockerXs.AddRange(clefs
            .Where(c => c.PartNumber == part && c.MeasureNumber == measure)
            .Select(c => CenterX(c.LogicalBounds))
            .Where(x => x.HasValue)
            .Select(x => x!.Value));
        blockerXs.AddRange(meters
            .Where(m => m.PartNumber == part && m.MeasureNumber == measure)
            .Select(m => CenterX(block.ToLogical(m.PhysicalBounds)))
            .Where(x => x.HasValue)
            .Select(x => x!.Value));
        blockerXs.AddRange(alreadyResolved
            .Where(a => a.PartNumber == part && a.MeasureNumber == measure)
            .Select(a => CenterX(a.LogicalBounds))
            .Where(x => x.HasValue)
            .Select(x => x!.Value));

        // Other noteheads count too: an intervening note means this accidental cannot jump over it.
        blockerXs.AddRange(rightNotes.Skip(1).Select(x => x.X!.Value));

        return blockerXs.Any(b => b > accidentalX && b < targetX)
            ? null
            : nearest.Note;
    }

    private static double? CenterX(LogicalRectD b) =>
        b.Left is { } l && b.Right is { } r ? (l + r) / 2.0 : null;
}
