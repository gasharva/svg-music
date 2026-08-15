using System.Numerics;

namespace SvgSymbols.Services;

public enum ClefSymbol
{
    G,
    F,
    C
}

public sealed record ClefSymbolCandidate(
    ClefSymbol Symbol,
    double Distance,
    double Confidence);

public sealed record ClefSymbolRecognition(
    ClefSymbol? Symbol,
    double Confidence,
    IReadOnlyList<ClefSymbolCandidate> Candidates,
    string? Error = null);

public interface IClefRecognizer
{
    ClefSymbolRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours);
}

/// <summary>
/// Vector-only recognizer backed by repo-local Bravura clef glyphs.
/// The public contract accepts resolved contours only; it never reopens the score SVG.
/// </summary>
public sealed class BravuraClefRecognizer : IClefRecognizer
{
    private readonly SvgShapeNormalizer _normalizer = new();
    private readonly FourierDescriptorAnalyzer _fourier = new();
    private readonly FourierDescriptorComparer _comparer = new();
    private readonly IReadOnlyList<Reference> _references;

    public BravuraClefRecognizer(string referenceGlyphDirectory, string workDirectory)
    {
        Directory.CreateDirectory(workDirectory);
        _references = new[]
        {
            BuildReference(ClefSymbol.G, Path.Combine(referenceGlyphDirectory, "gClef.svg"), workDirectory),
            BuildReference(ClefSymbol.F, Path.Combine(referenceGlyphDirectory, "fClef.svg"), workDirectory)
        };
    }

    public ClefSymbolRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new ClefSymbolRecognition(null, 0, Array.Empty<ClefSymbolCandidate>(), "No contours supplied.");

        var temporary = Path.Combine(Path.GetTempPath(), $"svg-clef-{Guid.NewGuid():N}.svg");
        try
        {
            using var normalized = _normalizer.NormalizeContours(contours);
            _normalizer.WriteNormalizedPath(normalized, temporary);
            var descriptor = _fourier.Analyze(temporary);

            var ranked = _references
                .Select(reference =>
                {
                    var complex = _comparer.ComplexDistance(descriptor, reference.Descriptor);
                    var magnitude = _comparer.MagnitudeDistance(descriptor, reference.Descriptor);
                    return new
                    {
                        reference.Symbol,
                        Distance = complex + 0.20 * magnitude
                    };
                })
                .OrderBy(x => x.Distance)
                .ToArray();

            if (ranked.Length == 0)
                return new ClefSymbolRecognition(null, 0, Array.Empty<ClefSymbolCandidate>(), "No clef references.");

            const double temperature = 1.6;
            var min = ranked[0].Distance;
            var weights = ranked.Select(x => Math.Exp(-(x.Distance - min) / temperature)).ToArray();
            var total = Math.Max(1e-12, weights.Sum());
            var absoluteQuality = Math.Exp(-min / 6.0);

            var candidates = ranked
                .Select((x, i) => new ClefSymbolCandidate(
                    x.Symbol,
                    x.Distance,
                    Math.Clamp(weights[i] / total * absoluteQuality, 0d, 1d)))
                .ToArray();

            var best = candidates[0];
            return new ClefSymbolRecognition(best.Symbol, best.Confidence, candidates);
        }
        catch (Exception ex)
        {
            return new ClefSymbolRecognition(null, 0, Array.Empty<ClefSymbolCandidate>(), ex.Message);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { }
        }
    }

    private Reference BuildReference(ClefSymbol symbol, string sourcePath, string workDirectory)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Bravura clef reference not found.", sourcePath);

        var normalizedPath = Path.Combine(workDirectory, $"{symbol}-clef.normalized.svg");
        _normalizer.NormalizeToFile(sourcePath, normalizedPath);
        return new Reference(symbol, _fourier.Analyze(normalizedPath));
    }

    private sealed record Reference(ClefSymbol Symbol, FourierDescriptor Descriptor);
}
