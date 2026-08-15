using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed record SvgNumberCandidate(int Value, double Confidence);

public sealed record SvgNumberRecognition(
    int? Value,
    double Confidence,
    IReadOnlyList<SvgNumberCandidate> Candidates,
    string? Error = null);

/// <summary>
/// Reusable boundary around the experimental vector-number recognizer.
/// Callers provide already-resolved vector contours. No source SVG access is allowed here.
/// </summary>
public interface ISvgNumberRecognizer
{
    SvgNumberRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours);
}

/// <summary>
/// Production-facing adapter over the same DigitTopologyAnalyzer that is used by the
/// SvgSymbols gallery. References are built once from repo-local Bravura/SMuFL timeSig0..9.
/// Score recognition itself works only with vector contours supplied by pipeline step 2.
/// </summary>
public sealed class BravuraNumberRecognizer : ISvgNumberRecognizer
{
    private readonly DigitTopologyAnalyzer _analyzer = new();
    private readonly IReadOnlyList<(SymbolSource Source, string Path)> _corpus;
    private readonly string _workDirectory;

    public BravuraNumberRecognizer(string referenceGlyphDirectory, string workDirectory)
    {
        _workDirectory = workDirectory;
        Directory.CreateDirectory(_workDirectory);

        var referenceDirectory = Path.Combine(workDirectory, "references");
        Directory.CreateDirectory(referenceDirectory);
        _corpus = BuildCorpus(referenceGlyphDirectory, referenceDirectory);
    }

    public SvgNumberRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new SvgNumberRecognition(null, 0, Array.Empty<SvgNumberCandidate>(), "No contours supplied.");

        var tempPath = Path.Combine(_workDirectory, $"candidate-{Guid.NewGuid():N}.svg");
        try
        {
            WriteContoursSvg(contours, tempPath);
            var result = _analyzer.Analyze(tempPath, _corpus);

            int? value = int.TryParse(result.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

            var candidates = BuildCandidates(result);
            return new SvgNumberRecognition(value, result.Probability, candidates, result.Error);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }
        }
    }

    private static IReadOnlyList<SvgNumberCandidate> BuildCandidates(NumberRecognition result)
    {
        if (result.Digits.Count == 0)
            return Array.Empty<SvgNumberCandidate>();

        if (result.Digits.Count == 1)
        {
            return result.Digits[0].Candidates
                .Select(x => new SvgNumberCandidate(x.Digit, x.Probability))
                .OrderByDescending(x => x.Confidence)
                .ToArray();
        }

        // Meter numbers are at most two digits in our current corpus. Preserve alternatives so
        // MeterResolver can apply the small set of musically valid time-signature pairs.
        return result.Digits[0].Candidates
            .SelectMany(left => result.Digits[1].Candidates.Select(right =>
                new SvgNumberCandidate(
                    left.Digit * 10 + right.Digit,
                    Math.Sqrt(left.Probability * right.Probability))))
            .GroupBy(x => x.Value)
            .Select(x => x.OrderByDescending(c => c.Confidence).First())
            .OrderByDescending(x => x.Confidence)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyList<(SymbolSource Source, string Path)> BuildCorpus(
        string referenceGlyphDirectory,
        string outputDirectory)
    {
        var result = new List<(SymbolSource Source, string Path)>();

        for (var digit = 0; digit <= 9; digit++)
        {
            var outputName = $"Bravura-{digit}.svg";
            var outputPath = Path.Combine(outputDirectory, outputName);
            ComposeBravuraNumber(referenceGlyphDirectory, digit.ToString(CultureInfo.InvariantCulture), outputPath);

            result.Add((
                new SymbolSource(
                    Kind: "Rhythm",
                    Category: "Time-signature number / Bravura",
                    Title: $"Bravura {digit}",
                    FileName: outputName,
                    DescriptionUrl: "#",
                    FileUrl: outputPath,
                    License: "SIL OFL 1.1 (Bravura)",
                    LicenseUrl: null,
                    Artist: "Bravura / SMuFL"),
                outputPath));
        }

        return result;
    }

    private static void WriteContoursSvg(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        string outputPath)
    {
        var usable = contours.Where(x => x.Count >= 3).ToArray();
        if (usable.Length == 0)
            throw new InvalidOperationException("No usable contours supplied.");

        var points = usable.SelectMany(x => x).ToArray();
        var minX = points.Min(x => x.X);
        var minY = points.Min(x => x.Y);
        var maxX = points.Max(x => x.X);
        var maxY = points.Max(x => x.Y);
        var width = Math.Max(1e-6f, maxX - minX);
        var height = Math.Max(1e-6f, maxY - minY);
        var padding = Math.Max(width, height) * 0.03f;

        var d = new StringBuilder();
        foreach (var contour in usable)
        {
            d.Append("M ").Append(Fmt(contour[0].X)).Append(' ').Append(Fmt(contour[0].Y));
            for (var i = 1; i < contour.Count; i++)
                d.Append(" L ").Append(Fmt(contour[i].X)).Append(' ').Append(Fmt(contour[i].Y));
            d.Append(" Z ");
        }

        XNamespace ns = "http://www.w3.org/2000/svg";
        var root = new XElement(ns + "svg",
            new XAttribute("viewBox", string.Join(" ",
                Fmt(minX - padding),
                Fmt(minY - padding),
                Fmt(width + 2 * padding),
                Fmt(height + 2 * padding))),
            new XElement(ns + "path",
                new XAttribute("d", d.ToString()),
                new XAttribute("fill", "black"),
                new XAttribute("fill-rule", "evenodd")));

        new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(outputPath);
    }

    private static void ComposeBravuraNumber(string sourceDirectory, string value, string outputPath)
    {
        const double targetHeight = 1000d;
        const double gap = 35d;

        var glyphs = value
            .Select(ch => LoadGlyph(Path.Combine(sourceDirectory, $"timeSig{ch}.svg")))
            .ToArray();

        var x = 0d;
        var placements = new List<(Glyph Glyph, double X, double Scale)>();
        foreach (var glyph in glyphs)
        {
            var scale = targetHeight / glyph.Height;
            placements.Add((glyph, x, scale));
            x += glyph.Width * scale + gap;
        }

        var totalWidth = Math.Max(1d, x - gap);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var root = new XElement(ns + "svg",
            new XAttribute("viewBox", $"0 0 {Fmt(totalWidth)} {Fmt(targetHeight)}"));

        foreach (var placement in placements)
        {
            var maxY = placement.Glyph.MinY + placement.Glyph.Height;
            var group = new XElement(ns + "g",
                new XAttribute(
                    "transform",
                    $"translate({Fmt(placement.X)} 0) scale({Fmt(placement.Scale)} {Fmt(-placement.Scale)}) translate({Fmt(-placement.Glyph.MinX)} {Fmt(-maxY)})"));
            foreach (var node in placement.Glyph.Content)
                group.Add(new XElement(node));
            root.Add(group);
        }

        new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(outputPath);
    }

    private static Glyph LoadGlyph(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Bravura time-signature glyph not found.", path);

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidOperationException($"SVG root not found: {path}");
        var viewBox = ((string?)root.Attribute("viewBox"))?
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.Parse(x, CultureInfo.InvariantCulture))
            .ToArray();

        if (viewBox is null || viewBox.Length != 4 || viewBox[2] <= 0 || viewBox[3] <= 0)
            throw new InvalidOperationException($"Invalid SVG viewBox: {path}");

        return new Glyph(
            viewBox[0], viewBox[1], viewBox[2], viewBox[3],
            root.Elements().Select(x => new XElement(x)).ToArray());
    }

    private static string Fmt(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Fmt(float value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record Glyph(
        double MinX,
        double MinY,
        double Width,
        double Height,
        IReadOnlyList<XElement> Content);
}
