using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Finds clefs from MusicSymbolResolver candidates. Position inside the measure is deliberately not
/// used as a prior: clef changes may occur anywhere. Recognition consumes only smooth source geometry.
/// </summary>
public sealed class ClefResolver
{
    private readonly IClefRecognizer _recognizer;
    private readonly ClefCandidateSanity _sanity;
    private readonly double _minimumConfidence;

    public ClefResolver(
        IClefRecognizer recognizer,
        ClefCandidateSanity? sanity = null,
        double minimumConfidence = 0.0)
    {
        _recognizer = recognizer;
        _sanity = sanity ?? new ClefCandidateSanity();
        _minimumConfidence = minimumConfidence;
    }

    public IReadOnlyList<ClefResolution> Resolve(
        PartMeasureBlock block,
        MusicSymbolResolution symbols,
        LogicalGridResolution grid)
    {
        if (!grid.TryGetBlock(block.PartNumber, block.MeasureNumber, out var logicalBlock))
            return Array.Empty<ClefResolution>();

        var staffHeight = Math.Max(1e-9, block.PhysicalBounds.Height);
        var available = symbols.Candidates
            .Where(x =>
                x.Scope == PrimitiveLogicalScope.PartMeasure &&
                x.PartNumber == block.PartNumber &&
                x.MeasureNumber == block.MeasureNumber)
            .Where(x => x.SmoothPaths.Count > 0)
            .OrderBy(x => x.PhysicalBounds.Left)
            .ToArray();

        if (available.Length == 0)
            return Array.Empty<ClefResolution>();

        var recognized = new List<ScoredClef>();
        foreach (var candidate in BuildCandidates(available, logicalBlock, staffHeight))
        {
            var logicalBounds = logicalBlock.ToLogical(candidate.Bounds);
            if (!_sanity.Accept(logicalBounds, candidate.Bounds, staffHeight))
                continue;

            if (_recognizer is DiagnosticClefRecognizer diagnostic)
            {
                diagnostic.SetNextContext(new ClefDiagnosticContext(
                    block.PartNumber,
                    block.MeasureNumber,
                    logicalBounds));
            }

            var contours = SmoothSymbolContourConverter.ToContours(new[] { candidate.Symbol });
            if (contours.Count == 0)
                continue;

            var recognition = _recognizer.Recognize(contours);
            if (recognition.Symbol is null || recognition.Confidence < _minimumConfidence)
                continue;

            var kind = recognition.Symbol.Value switch
            {
                ClefSymbol.G => ClefKind.G,
                ClefSymbol.F => ClefKind.F,
                ClefSymbol.C => ClefKind.C,
                _ => throw new ArgumentOutOfRangeException()
            };

            recognized.Add(new ScoredClef(
                new ClefResolution(
                    block.PartNumber,
                    block.MeasureNumber,
                    kind,
                    recognition.Confidence,
                    candidate.Bounds,
                    logicalBounds),
                recognition.Confidence));
        }

        var result = new List<ClefResolution>();
        foreach (var item in recognized.OrderByDescending(x => x.Score))
        {
            if (result.Any(existing =>
                    existing.Kind == item.Clef.Kind &&
                    OverlapRatio(existing.PhysicalBounds, item.Clef.PhysicalBounds) >= 0.45))
                continue;

            result.Add(item.Clef);
        }

        return result.OrderBy(x => x.PhysicalBounds.Left).ToArray();
    }

    private IReadOnlyList<Candidate> BuildCandidates(
        IReadOnlyList<MusicSymbolCandidate> symbols,
        LogicalGridBlock logicalBlock,
        double staffHeight)
    {
        var result = new List<Candidate>();

        foreach (var symbol in symbols)
        {
            var logical = logicalBlock.ToLogical(symbol.PhysicalBounds);
            if (!_sanity.Accept(logical, symbol.PhysicalBounds, staffHeight))
                continue;

            result.Add(new Candidate(symbol, symbol.PhysicalBounds));
        }

        return result
            .GroupBy(x => (
                X: Math.Round(x.Bounds.CenterX, 2),
                Y: Math.Round(x.Bounds.CenterY, 2),
                W: Math.Round(x.Bounds.Width, 2),
                H: Math.Round(x.Bounds.Height, 2)))
            .Select(x => x
                .OrderBy(candidate => candidate.Symbol.IsDerived)
                .ThenByDescending(candidate => candidate.Symbol.SmoothPaths.Count)
                .First())
            .ToArray();
    }

    private static double OverlapRatio(RectD a, RectD b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top)
            return 0d;

        var intersection = (right - left) * (bottom - top);
        var smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return intersection / Math.Max(1e-9, smaller);
    }

    private sealed record Candidate(
        MusicSymbolCandidate Symbol,
        RectD Bounds);

    private sealed record ScoredClef(ClefResolution Clef, double Score);
}
