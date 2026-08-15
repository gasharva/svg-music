using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using SvgSymbols.Models;
using Skia = SkiaSharp;

namespace SvgSymbols.Services;

public sealed record NumberCandidate(
    int Value,
    double Distance,
    double Probability,
    string BestReference,
    double? StructuralDistance = null,
    double? FourierDistance = null,
    double? ComplexFourierDistance = null);

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

public sealed class NormalizedNumberClassifier
{
    private static readonly Regex NumberFileName = new(
        @"^(?:Music|Bravura-)(?<value>\d+)\.svg$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const double Temperature = 0.70;
    private const int SeparatorSamples = 96;
    private const double MinimumSeparatorWidthRatio = 0.025;
    private const double MinimumSideWidthRatio = 0.12;

    // Magnitude remains useful but phase-aware Fourier now gets a real vote too.
    // Keep both weaker than topology so font style cannot dominate the decision.
    private const double MagnitudeFourierWeight = 0.15;
    private const double ComplexFourierWeight = 0.20;

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
        Skia.SKPath normalized,
        IReadOnlyList<NumberReferenceModel> model,
        string? excludeFileName)
    {
        var usable = model
            .Where(x => string.IsNullOrWhiteSpace(excludeFileName) ||
                        !string.Equals(x.FileName, excludeFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (usable.Length == 0)
            return new NumberClassification(null, 0, Array.Empty<NumberCandidate>(), "No usable number references.");

        var whole = ClassifyWhole(normalized, usable);
        var segmented = TryClassifyAsTwoDigits(normalized, usable);
        if (segmented is null)
            return whole;

        if (segmented.Confidence >= 0.12 || whole.Value is >= 10)
            return Merge(segmented, whole);

        return whole;
    }

    private NumberClassification ClassifyWhole(
        Skia.SKPath normalized,
        IReadOnlyList<NumberReferenceModel> usable)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"svg-number-classifier-{Guid.NewGuid():N}.svg");
        try
        {
            _normalizer.WriteNormalizedPath(normalized, temp);
            var candidateFeatures = _features.Extract(temp);
            var candidateFourier = _fourier.Analyze(temp);

            var nearestPerValue = usable
                .GroupBy(x => x.Value)
                .Select(group => group
                    .Select(reference =>
                    {
                        var parts = DistanceParts(candidateFeatures, candidateFourier, reference);
                        return new
                        {
                            Reference = reference,
                            parts.Structural,
                            parts.MagnitudeFourier,
                            parts.ComplexFourier,
                            parts.Combined
                        };
                    })
                    .OrderBy(x => x.Combined)
                    .First())
                .OrderBy(x => x.Combined)
                .ToArray();

            return BuildClassification(nearestPerValue
                .Select(x => (
                    x.Reference.Value,
                    x.Combined,
                    x.Reference.FileName,
                    (double?)x.Structural,
                    (double?)x.MagnitudeFourier,
                    (double?)x.ComplexFourier))
                .ToArray());
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private NumberClassification? TryClassifyAsTwoDigits(
        Skia.SKPath normalized,
        IReadOnlyList<NumberReferenceModel> usable)
    {
        var split = TrySplitAtFullHeightGap(normalized);
        if (split is null)
            return null;

        using var left = split.Value.Left;
        using var right = split.Value.Right;

        var singleDigitReferences = usable.Where(x => x.Value is >= 0 and <= 9).ToArray();
        if (singleDigitReferences.Length == 0)
            return null;

        var leftResult = ClassifyWhole(left, singleDigitReferences);
        var rightResult = ClassifyWhole(right, singleDigitReferences);
        if (leftResult.Value is null || rightResult.Value is null)
            return null;

        var value = leftResult.Value.Value * 10 + rightResult.Value.Value;
        if (!usable.Any(x => x.Value == value))
            return null;

        var confidence = Math.Sqrt(leftResult.Confidence * rightResult.Confidence);
        var distance = -Math.Log(Math.Max(confidence, 1e-9));

        var candidate = new NumberCandidate(
            value,
            distance,
            confidence,
            $"digits {leftResult.Value}+{rightResult.Value}");

        return new NumberClassification(value, confidence, new[] { candidate });
    }

    private (Skia.SKPath Left, Skia.SKPath Right)? TrySplitAtFullHeightGap(Skia.SKPath path)
    {
        var bounds = path.Bounds;
        if (bounds.Width <= 1e-6f || bounds.Height <= 1e-6f)
            return null;

        var sampleWidth = bounds.Width / SeparatorSamples;
        var empty = new bool[SeparatorSamples];

        for (var i = 1; i < SeparatorSamples - 1; i++)
        {
            var x0 = bounds.Left + i * sampleWidth;
            var x1 = x0 + sampleWidth;

            using var strip = new Skia.SKPath();
            strip.AddRect(new Skia.SKRect(x0, bounds.Top, x1, bounds.Bottom));
            using var intersection = path.Op(strip, Skia.SKPathOp.Intersect);
            empty[i] = intersection is null || intersection.IsEmpty;
        }

        var bestStart = -1;
        var bestEnd = -1;
        var runStart = -1;

        for (var i = 1; i < SeparatorSamples - 1; i++)
        {
            if (empty[i])
            {
                if (runStart < 0)
                    runStart = i;
                continue;
            }

            if (runStart >= 0)
            {
                if (bestStart < 0 || i - runStart > bestEnd - bestStart)
                {
                    bestStart = runStart;
                    bestEnd = i;
                }
                runStart = -1;
            }
        }

        if (runStart >= 0 && (bestStart < 0 || SeparatorSamples - 1 - runStart > bestEnd - bestStart))
        {
            bestStart = runStart;
            bestEnd = SeparatorSamples - 1;
        }

        if (bestStart < 0)
            return null;

        var gapWidth = (bestEnd - bestStart) * sampleWidth;
        if (gapWidth / bounds.Width < MinimumSeparatorWidthRatio)
            return null;

        var splitX = bounds.Left + (bestStart + bestEnd) * 0.5f * sampleWidth;
        if ((splitX - bounds.Left) / bounds.Width < MinimumSideWidthRatio ||
            (bounds.Right - splitX) / bounds.Width < MinimumSideWidthRatio)
            return null;

        using var leftRect = new Skia.SKPath();
        leftRect.AddRect(new Skia.SKRect(bounds.Left, bounds.Top, splitX, bounds.Bottom));
        using var rightRect = new Skia.SKPath();
        rightRect.AddRect(new Skia.SKRect(splitX, bounds.Top, bounds.Right, bounds.Bottom));

        var left = path.Op(leftRect, Skia.SKPathOp.Intersect);
        var right = path.Op(rightRect, Skia.SKPathOp.Intersect);
        if (left is null || right is null || left.IsEmpty || right.IsEmpty)
        {
            left?.Dispose();
            right?.Dispose();
            return null;
        }

        return (left, right);
    }

    private static NumberClassification Merge(NumberClassification segmented, NumberClassification whole)
    {
        var byValue = segmented.Candidates
            .Concat(whole.Candidates)
            .GroupBy(x => x.Value)
            .Select(group => group.OrderByDescending(x => x.Probability).First())
            .OrderByDescending(x => x.Probability)
            .Take(5)
            .ToArray();

        var winner = byValue[0];
        return new NumberClassification(winner.Value, winner.Probability, byValue);
    }

    private NumberClassification BuildClassification(
        IReadOnlyList<(
            int Value,
            double Distance,
            string Reference,
            double? Structural,
            double? MagnitudeFourier,
            double? ComplexFourier)> nearestPerValue)
    {
        if (nearestPerValue.Count == 0)
            return new NumberClassification(null, 0, Array.Empty<NumberCandidate>(), "No comparable number references.");

        var probabilities = Softmax(nearestPerValue.Select(x => x.Distance).ToArray());
        var bestDistance = nearestPerValue[0].Distance;
        var absoluteQuality = Math.Exp(-bestDistance / 4.0);

        var candidates = nearestPerValue
            .Select((x, i) => new NumberCandidate(
                x.Value,
                x.Distance,
                Math.Clamp(probabilities[i] * absoluteQuality, 0d, 1d),
                x.Reference,
                x.Structural,
                x.MagnitudeFourier,
                x.ComplexFourier))
            .OrderByDescending(x => x.Probability)
            .Take(5)
            .ToArray();

        var best = candidates[0];
        return new NumberClassification(best.Value, best.Probability, candidates);
    }

    private (double Structural, double MagnitudeFourier, double ComplexFourier, double Combined) DistanceParts(
        DigitStructuralFeatures a,
        FourierDescriptor aFourier,
        NumberReferenceModel reference)
    {
        var b = reference.Features;

        var structural = 0d;
        structural += 4.00 * Square(a.HoleCount - b.HoleCount);
        structural += 0.35 * Square(Math.Min(a.OuterContourCount, 8) - Math.Min(b.OuterContourCount, 8));
        structural += 1.80 * Square(LogRatio(a.AspectRatio, b.AspectRatio));
        structural += 2.20 * Square(a.FillRatio - b.FillRatio);
        structural += 0.80 * Square(a.NormalizedPerimeter - b.NormalizedPerimeter);

        var holeCount = Math.Min(a.Holes.Count, b.Holes.Count);
        for (var i = 0; i < holeCount; i++)
        {
            structural += 2.2 * Square(a.Holes[i].CenterX - b.Holes[i].CenterX);
            structural += 4.0 * Square(a.Holes[i].CenterY - b.Holes[i].CenterY);
            structural += 1.0 * Square(a.Holes[i].AreaRatio - b.Holes[i].AreaRatio);
        }

        var magnitudeFourier = Math.Min(
            _fourierComparer.MagnitudeDistance(aFourier, reference.Fourier),
            20d);
        var complexFourier = Math.Min(
            _fourierComparer.ComplexDistance(aFourier, reference.Fourier),
            20d);

        var combined = structural
            + MagnitudeFourierWeight * magnitudeFourier
            + ComplexFourierWeight * complexFourier;

        return (structural, magnitudeFourier, complexFourier, combined);
    }

    private static double[] Softmax(IReadOnlyList<double> distances)
    {
        var min = distances.Min();
        var weights = distances.Select(x => Math.Exp(-(x - min) / Temperature)).ToArray();
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static double Square(double value) => value * value;
}
