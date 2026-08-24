using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Resolves accidentals from smooth MusicSymbol candidates.
/// </summary>
public sealed class AccidentalResolver
{
    private readonly GlyphPcaAccidentalRecognizer _recognizer;

    public double MinimumConfidence { get; init; } = 0.70;
    public double MaxAttachedNoteGapLogicalX { get; init; } = 8.0;
    public double NoteSearchLaneTolerance { get; init; } = 1.0;
    public double AttachmentPitchTolerance { get; init; } = 1.0;
    public double AttachmentGlyphLaneTolerance { get; init; } = 0.75;
    public double AttachmentHorizontalOverlapTolerance { get; init; } = 1.0;
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

        foreach (var group in prepared.GroupBy(x => (x.PartNumber, x.MeasureNumber)).OrderBy(x => x.Key.MeasureNumber).ThenBy(x => x.Key.PartNumber))
        {
            foreach (var lane in BuildVerticalLanes(group.ToArray()))
            {
                foreach (var candidate in lane.OrderBy(x => x.X))
                {
                    if (OverlapsClef(candidate, clefs))
                        continue;

                    // One unrelated symbol must not terminate the whole lane. In real SVGs a sharp
                    // may be preceded by another smooth candidate which simply is not an accidental.
                    var recognition = Recognize(candidate, recognized);
                    if (recognition is null)
                        continue;

                    found[recognition.SymbolId] = recognition;
                }
            }
        }

        foreach (var note in noteHeads.OrderBy(x => x.MeasureNumber).ThenBy(x => x.PartNumber).ThenBy(x => CenterX(x.LogicalBounds) ?? double.MaxValue))
        {
            var noteX = CenterX(note.LogicalBounds);
            if (noteX is null)
                continue;

            var noteY = CenterY(note.LogicalBounds);
            var left = prepared
                .Where(x => x.PartNumber == note.PartNumber && x.MeasureNumber == note.MeasureNumber)
                .Where(x => x.X < noteX.Value)
                .Where(x => VerticalDistanceToY(x.LogicalBounds, noteY) <= NoteSearchLaneTolerance)
                .OrderByDescending(x => x.X)
                .ToArray();

            foreach (var candidate in left)
            {
                if (found.ContainsKey(candidate.Symbol.Id))
                    continue;
                if (OverlapsClef(candidate, clefs))
                    continue;

                var recognition = Recognize(candidate, recognized);
                if (recognition is null)
                    continue;

                found[recognition.SymbolId] = recognition;
            }
        }

        return found.Values
            .OrderBy(x => x.MeasureNumber).ThenBy(x => x.PartNumber).ThenBy(x => x.X)
            .Select(accidental => new AccidentalResolution(
                accidental.PartNumber,
                accidental.MeasureNumber,
                accidental.LogicalBounds,
                accidental.PhysicalBounds,
                accidental.Kind,
                accidental.Confidence,
                FindAttachedNote(accidental, noteHeads)))
            .ToArray();
    }

    private IReadOnlyList<PreparedCandidate> PrepareCandidates(MusicSymbolResolution symbols, LogicalGridResolution grid)
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
            result.Add(new PreparedCandidate(symbol, part, symbol.MeasureNumber, logical, x.Value, CenterY(logical)));
        }
        return result;
    }

    private IReadOnlyList<IReadOnlyList<PreparedCandidate>> BuildVerticalLanes(IReadOnlyList<PreparedCandidate> candidates)
    {
        var lanes = new List<List<PreparedCandidate>>();
        foreach (var candidate in candidates.OrderBy(x => x.CenterY))
        {
            var lane = lanes.FirstOrDefault(x => Math.Abs(x.Average(c => c.CenterY) - candidate.CenterY) <= LaneTolerance);
            if (lane is null)
            {
                lane = new List<PreparedCandidate>();
                lanes.Add(lane);
            }
            lane.Add(candidate);
        }
        return lanes;
    }

    private RecognizedCandidate? Recognize(PreparedCandidate candidate, IDictionary<int, RecognizedCandidate?> cache)
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
            candidate.Symbol.Id, candidate.PartNumber, candidate.MeasureNumber, candidate.LogicalBounds,
            candidate.Symbol.PhysicalBounds, candidate.X, recognition.Kind.Value, recognition.Confidence);
        cache[candidate.Symbol.Id] = result;
        return result;
    }

    private NoteHeadResolution? FindAttachedNote(RecognizedCandidate accidental, IReadOnlyList<NoteHeadResolution> noteHeads)
    {
        var anchorY = AccidentalPitchAnchorY(accidental.Kind, accidental.LogicalBounds);
        var anchorPosition = (int)Math.Round(anchorY);
        var accidentalRight = accidental.LogicalBounds.Right ?? accidental.X;

        return noteHeads
            .Where(x => x.PartNumber == accidental.PartNumber && x.MeasureNumber == accidental.MeasureNumber)
            .Select(x => new
            {
                Note = x,
                NoteLeft = x.LogicalBounds.Left ?? CenterX(x.LogicalBounds) ?? double.MaxValue,
                NoteCenterX = CenterX(x.LogicalBounds),
                Y = CenterY(x.LogicalBounds),
                Position = (int)Math.Round(CenterY(x.LogicalBounds))
            })
            .Where(x => x.NoteCenterX is not null)
            .Select(x => new
            {
                x.Note,
                x.NoteCenterX,
                XGap = x.NoteLeft - accidentalRight,
                GlyphYGap = VerticalDistanceToY(accidental.LogicalBounds, x.Y),
                AnchorYGap = Math.Abs(x.Y - anchorY),
                ExactPosition = x.Position == anchorPosition
            })
            .Where(x => x.XGap >= -AttachmentHorizontalOverlapTolerance && x.XGap <= MaxAttachedNoteGapLogicalX)
            .Where(x => x.GlyphYGap <= AttachmentGlyphLaneTolerance)
            // Pitch position is more important than tiny horizontal differences. The current Mimino
            // M3 flat is a concrete example: B4 is the exact staff position, while A4 is merely a bit
            // closer in X. Choosing X first attaches the sign to the wrong note.
            .OrderBy(x => x.ExactPosition ? 0 : 1)
            .ThenBy(x => x.AnchorYGap)
            .ThenBy(x => Math.Max(0.0, x.XGap))
            .ThenBy(x => x.NoteCenterX)
            .Select(x => x.Note)
            .FirstOrDefault();
    }

    private static double AccidentalPitchAnchorY(AccidentalKind kind, LogicalRectD bounds) => kind switch
    {
        AccidentalKind.Flat or AccidentalKind.DoubleFlat => bounds.Bottom - 1.0,
        _ => CenterY(bounds)
    };

    private static double VerticalDistanceToY(LogicalRectD bounds, double y)
    {
        if (y < bounds.Top) return bounds.Top - y;
        if (y > bounds.Bottom) return y - bounds.Bottom;
        return 0;
    }

    private static bool OverlapsClef(PreparedCandidate candidate, IReadOnlyList<ClefResolution> clefs) =>
        clefs.Any(c => c.PartNumber == candidate.PartNumber && c.MeasureNumber == candidate.MeasureNumber && c.PhysicalBounds.Intersects(candidate.Symbol.PhysicalBounds));

    private static double? CenterX(LogicalRectD b) => b.Left is { } l && b.Right is { } r ? (l + r) / 2.0 : null;
    private static double CenterY(LogicalRectD b) => (b.Top + b.Bottom) / 2.0;

    private sealed record PreparedCandidate(MusicSymbolCandidate Symbol, int PartNumber, int MeasureNumber,
        LogicalRectD LogicalBounds, double X, double CenterY);

    private sealed record RecognizedCandidate(int SymbolId, int PartNumber, int MeasureNumber,
        LogicalRectD LogicalBounds, RectD PhysicalBounds, double X, AccidentalKind Kind, double Confidence);
}
