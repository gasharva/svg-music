using System.Globalization;
using System.Xml.Linq;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed record SvgNumberRecognition(
    int? Value,
    double Confidence,
    string? Error = null);

/// <summary>
/// Small reusable boundary around the experimental vector-number recognizer.
/// Callers only provide an SVG file and a physical rectangle to inspect.
/// </summary>
public interface ISvgNumberRecognizer
{
    SvgNumberRecognition Recognize(string svgPath, double left, double top, double right, double bottom);
}

/// <summary>
/// Production-facing adapter over NormalizedNumberClassifier. The reference model is built once
/// from the repo-local Bravura/SMuFL timeSig glyphs; score code does not depend on classifier
/// internals or on the SvgSymbols experiment UI.
/// </summary>
public sealed class BravuraSvgNumberRecognizer : ISvgNumberRecognizer
{
    private static readonly int[] ReferenceValues = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16 };

    private readonly NormalizedNumberClassifier _classifier = new();
    private readonly IReadOnlyList<NumberReferenceModel> _model;
    private readonly string _cropDirectory;

    public BravuraSvgNumberRecognizer(string referenceGlyphDirectory, string workDirectory)
    {
        Directory.CreateDirectory(workDirectory);
        _cropDirectory = Path.Combine(workDirectory, "crops");
        Directory.CreateDirectory(_cropDirectory);

        var referenceDirectory = Path.Combine(workDirectory, "references");
        Directory.CreateDirectory(referenceDirectory);
        _model = BuildModel(referenceGlyphDirectory, referenceDirectory);
    }

    public SvgNumberRecognition Recognize(
        string svgPath,
        double left,
        double top,
        double right,
        double bottom)
    {
        if (right <= left || bottom <= top)
            return new SvgNumberRecognition(null, 0, "Empty recognition rectangle.");

        var temp = Path.Combine(_cropDirectory, $"crop-{Guid.NewGuid():N}.svg");
        try
        {
            var document = XDocument.Load(svgPath, LoadOptions.PreserveWhitespace);
            var root = document.Root
                ?? throw new InvalidOperationException("SVG root not found.");

            root.SetAttributeValue(
                "viewBox",
                string.Join(" ", new[] { left, top, right - left, bottom - top }
                    .Select(x => x.ToString("0.####", CultureInfo.InvariantCulture))));
            root.SetAttributeValue("width", null);
            root.SetAttributeValue("height", null);
            document.Save(temp);

            var result = _classifier.ClassifySvg(temp, _model);
            return new SvgNumberRecognition(result.Value, result.Confidence, result.Error);
        }
        catch (Exception ex)
        {
            return new SvgNumberRecognition(null, 0, ex.Message);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { }
        }
    }

    private static IReadOnlyList<NumberReferenceModel> BuildModel(
        string referenceGlyphDirectory,
        string outputDirectory)
    {
        var featureExtractor = new DigitStructuralFeatureExtractor();
        var fourier = new FourierDescriptorAnalyzer();
        var normalizer = new SvgShapeNormalizer();
        var result = new List<NumberReferenceModel>();

        foreach (var value in ReferenceValues)
        {
            var referencePath = Path.Combine(outputDirectory, $"Bravura-{value}.svg");
            ComposeBravuraNumber(referenceGlyphDirectory, value.ToString(CultureInfo.InvariantCulture), referencePath);

            var normalizedPath = Path.Combine(outputDirectory, $"Bravura-{value}.normalized.svg");
            normalizer.NormalizeToFile(referencePath, normalizedPath);
            result.Add(new NumberReferenceModel(
                value,
                Path.GetFileName(referencePath),
                featureExtractor.Extract(normalizedPath),
                fourier.Analyze(normalizedPath)));
        }

        return result;
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
            viewBox[0],
            viewBox[1],
            viewBox[2],
            viewBox[3],
            root.Elements().Select(x => new XElement(x)).ToArray());
    }

    private static string Fmt(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record Glyph(
        double MinX,
        double MinY,
        double Width,
        double Height,
        IReadOnlyList<XElement> Content);
}
