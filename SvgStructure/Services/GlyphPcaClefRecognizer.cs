using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GlyphPcaGallery.Models;
using GlyphPcaGallery.Services;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// IClefRecognizer adapter over the reusable GlyphPcaGallery runtime.
/// The trained model stays glyph-generic; this adapter translates the model's treble/bass
/// class vocabulary into the pipeline's ClefSymbol vocabulary.
/// </summary>
public sealed class GlyphPcaClefRecognizer : IClefRecognizer
{
    private readonly GlyphFingerprintAnalyzer _analyzer;
    private readonly string _workDirectory;

    public GlyphPcaClefRecognizer(string modelPath, string workDirectory)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Glyph PCA model not found.", modelPath);

        _workDirectory = workDirectory;
        Directory.CreateDirectory(_workDirectory);

        var json = File.ReadAllText(modelPath);
        var model = JsonSerializer.Deserialize<GlyphModel>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Could not deserialize glyph PCA model.");

        _analyzer = new GlyphFingerprintAnalyzer(model);
    }

    public ClefSymbolRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new ClefSymbolRecognition(null, 0, Array.Empty<ClefSymbolCandidate>(), "No contours supplied.");

        var tempPath = Path.Combine(_workDirectory, $"candidate-{Guid.NewGuid():N}.svg");
        try
        {
            WriteContoursSvg(contours, tempPath);
            var analysis = _analyzer.Analyze(tempPath, Path.GetFileName(tempPath));
            if (analysis is null)
                return new ClefSymbolRecognition(null, 0, Array.Empty<ClefSymbolCandidate>(), "Glyph analyzer returned no result.");

            if (analysis.Error is not null)
                return new ClefSymbolRecognition(null, 0, Array.Empty<ClefSymbolCandidate>(), analysis.Error);

            // As with meter digits, acceptance is global. If the best PCA class is a rest, flat,
            // notehead, etc., do not reinterpret a lower-ranked treble/bass match as a clef.
            var globalBest = analysis.Matches.FirstOrDefault();
            var globalBestClef = globalBest is null ? null : ParseClefClass(globalBest.Class);
            if (!analysis.Accepted || globalBestClef is null)
                return new ClefSymbolRecognition(null, 0, Array.Empty<ClefSymbolCandidate>());

            var candidates = analysis.Matches
                .Select(ToClefCandidate)
                .Where(x => x is not null)
                .Select(x => x!)
                .GroupBy(x => x.Symbol)
                .Select(x => x.OrderBy(c => c.Distance).First())
                .OrderBy(x => x.Distance)
                .ToArray();

            var best = candidates.FirstOrDefault();
            if (best is null)
                return new ClefSymbolRecognition(null, 0, Array.Empty<ClefSymbolCandidate>());

            return new ClefSymbolRecognition(best.Symbol, best.Confidence, candidates);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }
        }
    }

    private static ClefSymbolCandidate? ToClefCandidate(ClassMatch match)
    {
        var symbol = ParseClefClass(match.Class);
        if (symbol is null)
            return null;

        var confidence = 1.0 / (1.0 + Math.Max(0, match.Distance));
        return new ClefSymbolCandidate(symbol.Value, match.Distance, confidence);
    }

    private static ClefSymbol? ParseClefClass(string className)
    {
        if (className.Equals("treble", StringComparison.OrdinalIgnoreCase))
            return ClefSymbol.G;

        if (className.Equals("bass", StringComparison.OrdinalIgnoreCase))
            return ClefSymbol.F;

        return null;
    }

    private static void WriteContoursSvg(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        string outputPath)
    {
        var usable = contours
            .Where(x => x.Count >= 3)
            .ToArray();

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
            d.Append("M ")
                .Append(Fmt(contour[0].X))
                .Append(' ')
                .Append(Fmt(contour[0].Y));

            for (var i = 1; i < contour.Count; i++)
            {
                d.Append(" L ")
                    .Append(Fmt(contour[i].X))
                    .Append(' ')
                    .Append(Fmt(contour[i].Y));
            }

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

    private static string Fmt(float value) =>
        value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
