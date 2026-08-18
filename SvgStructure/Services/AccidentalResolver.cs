using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Resolves accidentals from smooth MusicSymbol candidates.
/// PCA is invoked only for candidates that are structurally eligible:
/// 1) the left-most symbol chain in each logical Y lane (clefs are skipped), or
/// 2) the first symbol chain immediately to the left of an already recognized note head.
/// </summary>
public sealed class AccidentalResolver
{
    private readonly GlyphPcaAccidentalRecognizer _recognizer;

    public double MinimumConfidence { get; init; } = 0.70;
    public double MaxAttachedNoteGapLogicalX { get; init; } = 4.0;
    public double MaxAttachedNoteYDistance { get; init; } = 0.80;
    public double MinLogicalHeight { get; init; } = 1.5;
    public double MaxLogicalHeight { get; init; } = 7.0;
    public double LaneTolerance { get; init; } = 0.55;

    public AccidentalResolver(GlyphPcaAccidentalRecognizer recognizer) => _recognizer = recognizer;

    public IReadOnlyList<AccidentalResolution> Resolve(
        MusicSymbolResolution symbols,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<MeterResolution> meters)
    {
        var prepared = PrepareCandidates(symbols, grid);
        var recognized = new Dictionary<int, RecognizedCandidate?>();
        var found = new Dictionary<int, RecognizedCandidate>();

        foreach (var group in prepared
                     .GroupBy(x => (x.PartNumber, x.MeasureNumber))
                     .OrderBy(x => x.Key.MeasureNumber)
                     .ThenBy(x => x.Key.PartNumber))
        {
            var lanes = BuildVerticalLanes(group.ToArray());
            foreach (var lane in lanes)
            {
                foreach (var candidate in lane.OrderBy(x => x.X))
                {
                    if (OverlapsClef(candidate, clefs))
                        continue;

                    var recognition = Recognize(candidate, recognized);
                    if (recognition is null)
                        break;

                    found[recognition.SymbolId] = recognition;
                }
            }
        }

        foreach (var note in noteHeads
                     .OrderBy(x => x.MeasureNumber)
                     .ThenBy(x => x.PartNumber)
                     .ThenBy(x => CenterX(x.LogicalBounds) ?? double.MaxValue))
        {
            var noteX = CenterX(note.LogicalBounds);
            if (noteX is null)
                continue;

            var left = prepared
                .Where(x => x.PartNumber == note.PartNumber && x.MeasureNumber == note.MeasureNumber)
                .Where(x => x.X < noteX.Value)
                .OrderByDescending(x => x.X)
                .ToArray();

            foreach (var candidate in left)
            {
                if (found.ContainsKey(candidate.Symbol.Id))
                    continue;

                if (OverlapsClef(candidate, clefs))
                    break;

                var recognition = Recognize(candidate, recognized);
                if (recognition is null)
                    break;

                found[recognition.SymbolId] = recognition;
            }
        }

        var results = new List<AccidentalResolution>();
        foreach (var accidental in found.Values
                     .OrderBy(x => x.MeasureNumber)
                     .ThenBy(x => x.PartNumber)
                     .ThenBy(x => x.X))
        {
            var attached = FindAttachedNote(accidental, noteHeads, prepared, found);
            results.Add(new AccidentalResolution(
                accidental.PartNumber,
                accidental.MeasureNumber,
                accidental.LogicalBounds,
                accidental.PhysicalBounds,
                accidental.Kind,
                accidental.Confidence,
                attached));
        }

        return results;
    }

    private IReadOnlyList<PreparedCandidate> PrepareCandidates(
        MusicSymbolResolution symbols,
        LogicalGridResolution grid)
    {
        var result = new List<PreparedCandidate>();

        foreach (var symbol in symbols.Candidates
                     .Where(x => x.Scope == PrimitiveLogicalScope.PartMeasure && x.PartNumber is not null)
                     .Where(x => x.SmoothPaths.Count > 0))
        {
            var part = symbol.PartNumber!.Value;
            if (!grid.TryGetBlock(part, symbol.MeasureNumber, out var block))
                continue;

            var logical = block.ToLogical(symbol.PhysicalBounds);
            var height = logical.Bottom - logical.Top;
            if (height < MinLogicalHeight || height > MaxLogicalHeight)
                continue;

            var x = CenterX(logical);
            if (x is null)
                continue;

            result.Add(new PreparedCandidate(
                symbol,
                part,
                symbol.MeasureNumber,
                logical,
                x.Value,
                (logical.Top + logical.Bottom) / 2.0));
        }

        return result;
    }

    private IReadOnlyList<IReadOnlyList<PreparedCandidate>> BuildVerticalLanes(
        IReadOnlyList<PreparedCandidate> candidates)
    {
        var lanes = new List<List<PreparedCandidate>>();

        foreach (var candidate in candidates.OrderBy(x => x.CenterY))
        {
            var lane = lanes.FirstOrDefault(x =>
                Math.Abs(x.Average(c => c.CenterY) - candidate.CenterY) <= LaneTolerance);

            if (lane is null)
            {
                lane = new List<PreparedCandidate>();
                lanes.Add(lane);
            }

            lane.Add(candidate);
        }

        return lanes;
    }

    private RecognizedCandidate? Recognize(
        PreparedCandidate candidate,
        IDictionary<int, RecognizedCandidate?> cache)
    {
        if (cache.TryGetValue(candidate.Symbol.Id, out var cached))
            return cached;

        var contours = SmoothSymbolContourConverter.ToContours(new[] { candidate.Symbol });
        if (contours.Count == 0)
        {
            cache[candidate.Symbol.Id] = null;
            return null;
        }

        var recognition = _recognizer.Recognize(contours);
        if (recognition.Kind is null || recognition.Confidence < MinimumConfidence)
        {
            cache[candidate.Symbol.Id] = null;
            return null;
        }

        var result = new RecognizedCandidate(
            candidate.Symbol.Id,
            candidate.PartNumber,
            candidate.MeasureNumber,
            candidate.LogicalBounds,
            candidate.Symbol.PhysicalBounds,
            candidate.X,
            recognition.Kind.Value,
            recognition.Confidence);

        cache[candidate.Symbol.Id] = result;
        return result;
    }

    private NoteHeadResolution? FindAttachedNote(
        RecognizedCandidate accidental,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<PreparedCandidate> allSymbols,
        IReadOnlyDictionary<int, RecognizedCandidate> recognizedAccidentals)
    {
        var anchorY = AccidentalPitchAnchorY(accidental.Kind, accidental.LogicalBounds);

        var candidates = noteHeads
            .Where(x => x.PartNumber == accidental.PartNumber && x.MeasureNumber == accidental.MeasureNumber)
            .Select(x => new
            {
                Note = x,
                X = CenterX(x.LogicalBounds),
                Y = (x.LogicalBounds.Top + x.LogicalBounds.Bottom) / 2.0
            })
            .Where(x => x.X is not null && x.X.Value > accidental.X)
            .Where(x => x.X!.Value - accidental.X <= MaxAttachedNoteGapLogicalX)
            .Where(x => Math.Abs(x.Y - anchorY) <= MaxAttachedNoteYDistance)
            .OrderBy(x => x.X)
            .ThenBy(x => Math.Abs(x.Y - anchorY))
            .ToArray();

        foreach (var candidate in candidates)
        {
            var targetX = candidate.X!.Value;

            // MusicSymbolResolver often emits a broader symbol candidate that contains the notehead
            // itself. Such a candidate may have its center to the left of the notehead center and used
            // to be mistaken for an intervening blocker. A symbol that overlaps the target head is part
            // of the destination note and is therefore transparent, just like recognized accidentals.
            var blocked = allSymbols
                .Where(x => x.PartNumber == accidental.PartNumber && x.MeasureNumber == accidental.MeasureNumber)
                .Where(x => x.X > accidental.X && x.X < targetX)
                .Where(x => !x.Symbol.PhysicalBounds.Intersects(candidate.Note.PhysicalBounds))
                .Any(x => !recognizedAccidentals.ContainsKey(x.Symbol.Id));

            if (!blocked)
                return candidate.Note;
        }

        return null;
    }

    private static double AccidentalPitchAnchorY(AccidentalKind kind, LogicalRectD bounds)
    {
        return kind switch
        {
            // The flat's pitch belongs to the bulb, not to the visual center of the tall stem.
            // On the real glyphs the bulb center is about one full staff-space above the bottom.
            // One staff-space equals two half-space logical units in this coordinate system.
            AccidentalKind.Flat or AccidentalKind.DoubleFlat => bounds.Bottom - 2.0,
            _ => (bounds.Top + bounds.Bottom) / 2.0
        };
    }

    private static bool OverlapsClef(
        PreparedCandidate candidate,
        IReadOnlyList<ClefResolution> clefs) =>
        clefs.Any(c =>
            c.PartNumber == candidate.PartNumber &&
            c.MeasureNumber == candidate.MeasureNumber &&
            c.PhysicalBounds.Intersects(candidate.Symbol.PhysicalBounds));

    private static double? CenterX(LogicalRectD b) =>
        b.Left is { } l && b.Right is { } r ? (l + r) / 2.0 : null;

    private sealed record PreparedCandidate(
        MusicSymbolCandidate Symbol,
        int PartNumber,
        int MeasureNumber,
        LogicalRectD LogicalBounds,
        double X,
        double CenterY);

    private sealed record RecognizedCandidate(
        int SymbolId,
        int PartNumber,
        int MeasureNumber,
        LogicalRectD LogicalBounds,
        RectD PhysicalBounds,
        double X,
        AccidentalKind Kind,
        double Confidence);
}
