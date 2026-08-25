using System.Globalization;
using System.Numerics;
using GlyphGeometry;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Conservative per-class acceptance limits for the class-mean geometry classifier.
/// Values come from the font-holdout grand-test "Nearest wrong score" column (16 points).
/// A shape whose distance is already beyond the closest known wrong-class boundary is not
/// allowed to become a semantic music object merely because it is the best member of a family.
/// </summary>
internal static class GeometryRecognitionThresholds
{
    private static readonly IReadOnlyDictionary<string, double> Values =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["gClef"] = 0.1524,
            ["fClef"] = 0.2046,

            ["timeSig0"] = 0.0429,
            ["timeSig1"] = 0.0376,
            ["timeSig2"] = 0.0519,
            ["timeSig3"] = 0.0485,
            ["timeSig4"] = 0.0504,
            ["timeSig5"] = 0.0651,
            ["timeSig6"] = 0.0458,
            ["timeSig7"] = 0.0589,
            ["timeSig8"] = 0.0417,
            ["timeSig9"] = 0.0412,

            ["accidentalFlat"] = 0.0547,
            ["accidentalSharp"] = 0.0603,
            ["accidentalNatural"] = 0.0599,
            ["accidentalDoubleSharp"] = 0.0892,
            ["accidentalDoubleFlat"] = 0.1131,

            ["flag8thUp"] = 0.0496,
            ["flag8thDown"] = 0.0518,
            ["flag16thUp"] = 0.0502,
            ["flag16thDown"] = 0.0532,
            ["flag32ndUp"] = 0.0440,
            ["flag32ndDown"] = 0.0438,

            ["restWhole"] = 0.0726,
            ["restHalf"] = 0.0929,
            ["restQuarter"] = 0.0589,
            ["rest8th"] = 0.0687,
            ["rest16th"] = 0.0424,
            ["rest32nd"] = 0.0457,
        };

    // The supplied holdout table has no observations for these rarer rest classes.
    // Keep a deliberately conservative fallback rather than silently accepting any nearest class.
    private const double UnknownClassThreshold = 0.0600;

    public static bool Accept(GlyphClassCandidate candidate) =>
        candidate.Distance <= Get(candidate.ClassName);

    public static double Get(string className) =>
        Values.TryGetValue(className, out var threshold) ? threshold : UnknownClassThreshold;
}

public sealed class GeometryClefRecognizer : IClefRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    public GeometryClefRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public ClefSymbolRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var matches = _classifier.Classify(contours, new[] { "gClef", "fClef" }, 4);
        var candidates = matches
            .Where(GeometryRecognitionThresholds.Accept)
            .Select(x => new ClefSymbolCandidate(
                x.ClassName.Equals("gClef", StringComparison.OrdinalIgnoreCase) ? ClefSymbol.G : ClefSymbol.F,
                x.Distance, x.Confidence))
            .ToArray();
        var best = candidates.FirstOrDefault();
        return best is null
            ? new(null, 0, candidates, ThresholdError(matches.FirstOrDefault(), "clef"))
            : new(best.Symbol, best.Confidence, candidates);
    }

    private static string ThresholdError(GlyphClassCandidate? best, string family) => best is null
        ? $"Geometry classifier returned no {family}."
        : $"Geometry {family} rejected: {best.ClassName} d={best.Distance:0.####} > {GeometryRecognitionThresholds.Get(best.ClassName):0.####}.";
}

public sealed class GeometryNumberRecognizer : ISvgNumberRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    private static readonly string[] Classes = Enumerable.Range(0, 10).Select(i => $"timeSig{i}").ToArray();
    public GeometryNumberRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public SvgNumberRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var matches = _classifier.Classify(contours, Classes, 10);
        var candidates = matches
            .Where(GeometryRecognitionThresholds.Accept)
            .Select(x => int.TryParse(x.ClassName.AsSpan("timeSig".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? new SvgNumberCandidate(n, x.Confidence) : null)
            .Where(x => x is not null).Select(x => x!).ToArray();
        var best = candidates.FirstOrDefault();
        return best is null
            ? new(null, 0, candidates, ThresholdError(matches.FirstOrDefault(), "meter digit"))
            : new(best.Value, best.Confidence, candidates);
    }

    private static string ThresholdError(GlyphClassCandidate? best, string family) => best is null
        ? $"Geometry classifier returned no {family}."
        : $"Geometry {family} rejected: {best.ClassName} d={best.Distance:0.####} > {GeometryRecognitionThresholds.Get(best.ClassName):0.####}.";
}

public sealed class GeometryAccidentalRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    private static readonly string[] Classes = { "accidentalFlat", "accidentalSharp", "accidentalNatural", "accidentalDoubleSharp", "accidentalDoubleFlat" };
    public GeometryAccidentalRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public AccidentalRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var matches = _classifier.Classify(contours, Classes, Classes.Length);
        var candidates = matches
            .Where(GeometryRecognitionThresholds.Accept)
            .Select(x => Parse(x.ClassName) is { } k ? new AccidentalCandidate(k, x.Distance, x.Confidence) : null)
            .Where(x => x is not null).Select(x => x!).ToArray();
        var best = candidates.FirstOrDefault();
        return best is null
            ? new(null, 0, candidates, ThresholdError(matches.FirstOrDefault(), "accidental"))
            : new(best.Kind, best.Confidence, candidates);
    }

    private static string ThresholdError(GlyphClassCandidate? best, string family) => best is null
        ? $"Geometry classifier returned no {family}."
        : $"Geometry {family} rejected: {best.ClassName} d={best.Distance:0.####} > {GeometryRecognitionThresholds.Get(best.ClassName):0.####}.";

    private static AccidentalKind? Parse(string s) => s.ToLowerInvariant() switch
    {
        "accidentalflat" => AccidentalKind.Flat,
        "accidentalsharp" => AccidentalKind.Sharp,
        "accidentalnatural" => AccidentalKind.Natural,
        "accidentaldoublesharp" => AccidentalKind.DoubleSharp,
        "accidentaldoubleflat" => AccidentalKind.DoubleFlat,
        _ => null
    };
}

public sealed class GeometryNoteFlagRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    private static readonly string[] Classes = { "flag8thUp", "flag8thDown", "flag16thUp", "flag16thDown", "flag32ndUp", "flag32ndDown" };
    public GeometryNoteFlagRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public NoteFlagRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var matches = _classifier.Classify(contours, Classes, Classes.Length);
        var parsed = matches
            .Where(GeometryRecognitionThresholds.Accept)
            .Select(x => Parse(x.ClassName) is { } p ? new NoteFlagCandidate(p.Item1, p.Item2, x.Distance, x.Confidence) : null)
            .Where(x => x is not null).Select(x => x!).ToArray();
        var best = parsed.FirstOrDefault();
        return best is null
            ? new(null, null, 0, parsed, ThresholdError(matches.FirstOrDefault(), "flag"))
            : new(best.Denominator, best.Direction, best.Confidence, parsed);
    }

    private static string ThresholdError(GlyphClassCandidate? best, string family) => best is null
        ? $"Geometry classifier returned no {family}."
        : $"Geometry {family} rejected: {best.ClassName} d={best.Distance:0.####} > {GeometryRecognitionThresholds.Get(best.ClassName):0.####}.";

    private static (int, StemDirection)? Parse(string s)
    {
        var n = s.ToLowerInvariant();
        var d = n.EndsWith("up") ? StemDirection.Up : n.EndsWith("down") ? StemDirection.Down : (StemDirection?)null;
        if (d is null) return null;
        if (n.StartsWith("flag8th")) return (8, d.Value);
        if (n.StartsWith("flag16th")) return (16, d.Value);
        if (n.StartsWith("flag32nd")) return (32, d.Value);
        return null;
    }
}

public sealed class GeometryRestRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    private static readonly string[] Classes =
    {
        "restWhole", "restHalf", "restQuarter", "rest8th", "rest16th", "rest32nd", "rest64th", "rest128th", "restBreve"
    };

    public GeometryRestRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public RestRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        // Restrict classification to the rest family up-front. Previously we classified against the
        // whole dataset and only then parsed rest names; that wasted work and made diagnostics noisy.
        var matches = _classifier.Classify(contours, Classes, Classes.Length);
        var parsed = matches
            .Where(GeometryRecognitionThresholds.Accept)
            .Select(x => Parse(x.ClassName) is { } d ? new RestCandidate(d, x.Distance, x.Confidence, x.ClassName) : null)
            .Where(x => x is not null).Select(x => x!).OrderBy(x => x.Distance).ToArray();
        var best = parsed.FirstOrDefault();
        return best is null
            ? new(null, 0, parsed, ThresholdError(matches.FirstOrDefault(), "rest"))
            : new(best.Denominator, best.Confidence, parsed);
    }

    private static string ThresholdError(GlyphClassCandidate? best, string family) => best is null
        ? $"Geometry classifier returned no {family}."
        : $"Geometry {family} rejected: {best.ClassName} d={best.Distance:0.####} > {GeometryRecognitionThresholds.Get(best.ClassName):0.####}.";

    private static int? Parse(string s)
    {
        var n = s.ToLowerInvariant().Replace("-", "").Replace("_", "");
        if (!n.StartsWith("rest")) return null;
        n = n[4..];
        return n switch
        {
            "whole" => 1,
            "half" => 2,
            "quarter" => 4,
            "eighth" or "8th" => 8,
            "16th" or "sixteenth" => 16,
            "32nd" or "thirtysecond" => 32,
            "64th" or "sixtyfourth" => 64,
            "128th" => 128,
            "breve" or "doublewhole" => 0,
            _ => null
        };
    }
}
