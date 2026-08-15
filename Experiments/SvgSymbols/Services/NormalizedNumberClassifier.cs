using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed record NumberCandidate(
    int Value,
    double Distance,
    double Probability,
    string BestReference);

public sealed record NumberClassification(
    int? Value,
    double Confidence,
    IReadOnlyList<NumberCandidate> Candidates,
    string? Error = null);

public sealed record NumberReferenceModel(
    int Value,
    string FileName,
    DigitStructuralFeatures Features,
    FourierDescriptor Fourier);

/// <summary>
/// Experimental whole-number classifier for time-signature numbers.
///
/// Important difference from the previous DigitTopologyAnalyzer: this classifier never tries
/// to split a candidate into digits. The complete raw silhouette is normalized with Skia
/// PathOps and compared against complete known numbers (0..9, 10, 12, 16, 32, ...).
/// This avoids confusing the hole in "0" with whitespace between two digits.
/// </summary>
public sealed class NormalizedNumberClassifier
{
    private static readonly Regex NumberFileName = new(
        @"^(?:Music|Bravura-)(?<value>\d+)\.svg$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const double Temperature = 0.70;

    private readonly SvgShapeNormalizer _normalizer = new();
    private readonly DigitStructuralFeatureExtractor _features = new();
    private readonly FourierDescriptorAnalyzer _fourier = new();
    private readonly FourierDescriptorComparer _fourierComparer = new();

    public IReadOnlyList<NumberReferenceModel> BuildModel(
        string outputRoot,
        IReadOnlyList<SymbolSource> rhythm)
    {
        var rhythmRoot = Path.Combine(outputRoot, "Samples", "Rhythm");
        var normalizedRoot = Path.Combine(rhythmRoot, "normalized-classifier");
        Directory.CreateDirectory(normalizedRoot);

        var result = new List<NumberReferenceModel>();

        foreach (var source in rhythm)
        {
            var fileName = Path.GetFileName(source.FileName);
            var match = NumberFileName.Match(fileName);
            if (!match.Success)
                continue;

            var sourcePath = Path.Combine(rhythmRoot, fileName);
            if (!File.Exists(sourcePath))
                continue;

            var normalizedPath = Path.Combine(normalizedRoot, fileName);
            try
            {
                _normalizer.NormalizeToFile(sourcePath, normalizedPath);
                result.Add(new NumberReferenceModel(
                    int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture),
                    fileName,
                    _features.Extract(normalizedPath),
                    _fourier.Analyze(normalizedPath)));
            }
            catch
            {
                // A single bad corpus item must not prevent the rest of the model from being built.
            }
        }

        return result;
    }

    public NumberClassification ClassifySvg(
        string sourcePath,
        IReadOnlyList<NumberReferenceModel> model,
        string? excludeFileName = null)
    {
        try
        {
            using var normalized = _normalizer.Normalize(sourcePath);
            return ClassifyNormalizedPath(normalized, model, excludeFileName);
        }
        catch (Exception ex)
        {
            return new NumberClassification(null, 0, Array.Empty<NumberCandidate>(), ex.Message);
        }
    }

    /// <summary>
    /// Production-shaped entry point: raw vector contours in one coordinate system go in;
    /// number + confidence come out. No SVG DOM and no rasterization are required here.
    /// </summary>
    public NumberClassification Classify(
        IReadOnlyList<IReadOnlyList<Vector2>> rawContours,
        IReadOnlyList<NumberReferenceModel> model)
    {
        try
        {
            using var normalized = _normalizer.NormalizeContours(rawContours);
            return ClassifyNormalizedPath(normalized, model, null);
        }
        catch (Exception ex)
        {
            return new NumberClassification(null, 0, Array.Empty<NumberCandidate>(), ex.Message);
        }
    }

    private NumberClassification ClassifyNormalizedPath(
        SkiaSharp.SKPath normalized,
        IReadOnlyList<NumberReferenceModel> model,
        string? excludeFileName)
    {
        var usable = model
            .Where(x => string.IsNullOrWhiteSpace(excludeFileName) ||
                        !string.Equals(x.FileName, excludeFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (usable.Length == 0)
            return new NumberClassification(null, 0, Array.Empty<NumberCandidate>(), "No usable number references.");

        var temp = Path.Combine(Path.GetTempPath(), $"svg-number-classifier-{Guid.NewGuid():N}.svg");
        try
        {
            _normalizer.WriteNormalizedPath(normalized, temp);
            var candidateFeatures = _features.Extract(temp);
            var candidateFourier = _fourier.Analyze(temp);

            var nearestPerValue = usable
                .GroupBy(x => x.Value)
                .Select(group => group
                    .Select(reference => new
                    {
                        Reference = reference,
                        Distance = Distance(candidateFeatures, candidateFourier, reference)
                    })
                    .OrderBy(x => x.Distance)
                    .First())
                .OrderBy(x => x.Distance)
                .ToArray();

            if (nearestPerValue.Length == 0)
                return new NumberClassification(null, 0, Array.Empty<NumberCandidate>(), "No comparable number references.");

            var probabilities = Softmax(nearestPerValue.Select(x => x.Distance).ToArray());
            var bestDistance = nearestPerValue[0].Distance;

            // Softmax only says "best among available classes". Penalize a winner that is
            // absolutely far from every known vector shape.
            var absoluteQuality = Math.Exp(-bestDistance / 4.0);

            var candidates = nearestPerValue
                .Select((x, i) => new NumberCandidate(
                    x.Reference.Value,
                    x.Distance,
                    Math.Clamp(probabilities[i] * absoluteQuality, 0d, 1d),
                    x.Reference.FileName))
                .OrderByDescending(x => x.Probability)
                .Take(5)
                .ToArray();

            var best = candidates[0];
            return new NumberClassification(best.Value, best.Probability, candidates);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { /* diagnostic experiment; temp cleanup failure is harmless */ }
        }
    }

    private double Distance(
        DigitStructuralFeatures a,
        FourierDescriptor aFourier,
        NumberReferenceModel reference)
    {
        var b = reference.Features;

        // Topology and proportions dominate. Fourier remains a weak tie-breaker because our
        // earlier experiment showed that its spectrum still carries a lot of font style.
        var distance = 0d;
        distance += 4.00 * Square(a.HoleCount - b.HoleCount);
        distance += 0.35 * Square(Math.Min(a.OuterContourCount, 8) - Math.Min(b.OuterContourCount, 8));
        distance += 1.80 * Square(LogRatio(a.AspectRatio, b.AspectRatio));
        distance += 2.20 * Square(a.FillRatio - b.FillRatio);
        distance += 0.80 * Square(a.NormalizedPerimeter - b.NormalizedPerimeter);

        var fourier = _fourierComparer.MagnitudeDistance(aFourier, reference.Fourier);
        distance += 0.30 * Math.Min(fourier, 20d);

        return distance;
    }

    private static double[] Softmax(IReadOnlyList<double> distances)
    {
        var min = distances.Min();
        var weights = distances
            .Select(x => Math.Exp(-(x - min) / Temperature))
            .ToArray();
        var total = weights.Sum();

        return total <= 1e-12
            ? Enumerable.Repeat(1d / weights.Length, weights.Length).ToArray()
            : weights.Select(x => x / total).ToArray();
    }

    private static double LogRatio(double a, double b)
    {
        a = Math.Max(a, 1e-9);
        b = Math.Max(b, 1e-9);
        return Math.Log(a / b);
    }

    private static double Square(double value) => value * value;
}
