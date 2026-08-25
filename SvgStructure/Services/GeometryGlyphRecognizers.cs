using System.Globalization;
using System.Numerics;
using GlyphGeometry;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

public sealed class GeometryClefRecognizer : IClefRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    public GeometryClefRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public ClefSymbolRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var matches = _classifier.Classify(contours, new[] { "gClef", "fClef" }, 4);
        var candidates = matches.Select(x => new ClefSymbolCandidate(
            x.ClassName.Equals("gClef", StringComparison.OrdinalIgnoreCase) ? ClefSymbol.G : ClefSymbol.F,
            x.Distance, x.Confidence)).ToArray();
        var best = candidates.FirstOrDefault();
        return best is null ? new(null,0,candidates,"Geometry classifier returned no clef.") : new(best.Symbol,best.Confidence,candidates);
    }
}

public sealed class GeometryNumberRecognizer : ISvgNumberRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    private static readonly string[] Classes = Enumerable.Range(0,10).Select(i=>$"timeSig{i}").ToArray();
    public GeometryNumberRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public SvgNumberRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var candidates = _classifier.Classify(contours, Classes, 10)
            .Select(x => int.TryParse(x.ClassName.AsSpan("timeSig".Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? new SvgNumberCandidate(n, x.Confidence) : null)
            .Where(x=>x is not null).Select(x=>x!).ToArray();
        var best=candidates.FirstOrDefault();
        return best is null ? new(null,0,candidates,"Geometry classifier returned no meter digit.") : new(best.Value,best.Confidence,candidates);
    }
}

public sealed class GeometryAccidentalRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    private static readonly string[] Classes = { "accidentalFlat", "accidentalSharp", "accidentalNatural", "accidentalDoubleSharp", "accidentalDoubleFlat" };
    public GeometryAccidentalRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public AccidentalRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var candidates = _classifier.Classify(contours, Classes, Classes.Length)
            .Select(x => Parse(x.ClassName) is { } k ? new AccidentalCandidate(k,x.Distance,x.Confidence) : null)
            .Where(x=>x is not null).Select(x=>x!).ToArray();
        var best=candidates.FirstOrDefault();
        return best is null ? new(null,0,candidates,"Geometry classifier returned no accidental.") : new(best.Kind,best.Confidence,candidates);
    }
    private static AccidentalKind? Parse(string s) => s.ToLowerInvariant() switch {
        "accidentalflat"=>AccidentalKind.Flat,"accidentalsharp"=>AccidentalKind.Sharp,"accidentalnatural"=>AccidentalKind.Natural,
        "accidentaldoublesharp"=>AccidentalKind.DoubleSharp,"accidentaldoubleflat"=>AccidentalKind.DoubleFlat,_=>null};
}

public sealed class GeometryNoteFlagRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    private static readonly string[] Classes = { "flag8thUp", "flag8thDown", "flag16thUp", "flag16thDown", "flag32ndUp", "flag32ndDown" };
    public GeometryNoteFlagRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public NoteFlagRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var parsed = _classifier.Classify(contours, Classes, Classes.Length)
            .Select(x => Parse(x.ClassName) is { } p ? new NoteFlagCandidate(p.Item1,p.Item2,x.Distance,x.Confidence) : null)
            .Where(x=>x is not null).Select(x=>x!).ToArray();
        var best=parsed.FirstOrDefault();
        return best is null ? new(null,null,0,parsed,"Geometry classifier returned no flag.") : new(best.Denominator,best.Direction,best.Confidence,parsed);
    }
    private static (int,StemDirection)? Parse(string s){var n=s.ToLowerInvariant();var d=n.EndsWith("up")?StemDirection.Up:n.EndsWith("down")?StemDirection.Down:(StemDirection?)null;if(d is null)return null; if(n.StartsWith("flag8th"))return(8,d.Value);if(n.StartsWith("flag16th"))return(16,d.Value);if(n.StartsWith("flag32nd"))return(32,d.Value);return null;}
}

public sealed class GeometryRestRecognizer
{
    private readonly GeometryGlyphClassifier _classifier;
    public GeometryRestRecognizer(GeometryGlyphClassifier classifier) => _classifier = classifier;
    public RestRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var parsed = _classifier.Classify(contours, null, 100)
            .Select(x => Parse(x.ClassName) is { } d ? new RestCandidate(d,x.Distance,x.Confidence,x.ClassName) : null)
            .Where(x=>x is not null).Select(x=>x!).OrderBy(x=>x.Distance).ToArray();
        var best=parsed.FirstOrDefault();
        return best is null ? new(null,0,parsed,"Geometry classifier returned no rest.") : new(best.Denominator,best.Confidence,parsed);
    }
    private static int? Parse(string s){var n=s.ToLowerInvariant().Replace("-","").Replace("_","");if(!n.StartsWith("rest"))return null;n=n[4..];return n switch{"whole"=>1,"half"=>2,"quarter"=>4,"eighth"=>8,"16th" or "sixteenth"=>16,"32nd" or "thirtysecond"=>32,"64th" or "sixtyfourth"=>64,"128th"=>128,"breve" or "doublewhole"=>0,_=>null};}
}
