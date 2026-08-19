using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GlyphPcaGallery.Models;
using GlyphPcaGallery.Services;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Adapter that lets MeterResolver use the reusable GlyphPcaGallery runtime without moving
/// the PCA/SDF implementation into SvgStructure. Candidate contours are written to a temporary
/// SVG because GlyphFingerprintAnalyzer intentionally owns SVG parsing/canonicalization.
/// </summary>
public sealed class GlyphPcaNumberRecognizer : ISvgNumberRecognizer
{
    private readonly GlyphFingerprintAnalyzer _analyzer;
    private readonly string _workDirectory;
    private readonly double _minimumConfidence;

    public GlyphPcaNumberRecognizer(
        string modelPath,
        string workDirectory,
        double minimumConfidence = 0.20)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Glyph PCA model not found.", modelPath);

        _workDirectory = workDirectory;
        _minimumConfidence = minimumConfidence;
        Directory.CreateDirectory(_workDirectory);

        var json = File.ReadAllText(modelPath);
        var model = JsonSerializer.Deserialize<GlyphModel>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Could not deserialize glyph PCA model.");

        _analyzer = new GlyphFingerprintAnalyzer(model);
    }

    public SvgNumberRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new SvgNumberRecognition(null, 0, Array.Empty<SvgNumberCandidate>(), "No contours supplied.");

        var tempPath = Path.Combine(_workDirectory, $"candidate-{Guid.NewGuid():N}.svg");
        try
        {
            WriteContoursSvg(contours, tempPath);
            var analysis = _analyzer.Analyze(tempPath, Path.GetFileName(tempPath));
            if (analysis is null)
                return new SvgNumberRecognition(null, 0, Array.Empty<SvgNumberCandidate>(), "Glyph analyzer returned no result.");

            if (analysis.Error is not null)
                return new SvgNumberRecognition(null, 0, Array.Empty<SvgNumberCandidate>(), analysis.Error);

            // Acceptance belongs to the global glyph classifier. Do not take a lower-ranked numeric
            // match when the model actually classified the shape as another glyph class.
            var globalBest = analysis.Matches.FirstOrDefault();
            var globalBestDigit = globalBest is null ? null : ParseDigitClass(globalBest.Class);
            if (!analysis.Accepted || globalBestDigit is null)
                return new SvgNumberRecognition(null, 0, Array.Empty<SvgNumberCandidate>());

            var candidates = analysis.Matches
                .Select(ToNumberCandidate)
                .Where(x => x is not null)
                .Select(x => x!)
                .GroupBy(x => x.Value)
                .Select(x => x.OrderByDescending(c => c.Confidence).First())
                .OrderByDescending(x => x.Confidence)
                .ToArray();

            var best = candidates.FirstOrDefault();
            if (best is null || best.Confidence < _minimumConfidence)
                return new SvgNumberRecognition(null, 0, Array.Empty<SvgNumberCandidate>());

            return new SvgNumberRecognition(best.Value, best.Confidence, candidates);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }
        }
    }

    private static SvgNumberCandidate? ToNumberCandidate(ClassMatch match)
    {
        var value = ParseDigitClass(match.Class);
        if (value is null)
            return null;

        // The PCA gallery exposes distance/risk rather than a probability. MeterResolver only
        // needs a monotonic confidence for ranking candidate pairs, so use a bounded inverse distance.
        var confidence = 1.0 / (1.0 + Math.Max(0, match.Distance));
        return new SvgNumberCandidate(value.Value, confidence);
    }

    private static int? ParseDigitClass(string className)
    {
        if (int.TryParse(className, NumberStyles.None, CultureInfo.InvariantCulture, out var plainDigit))
            return plainDigit;

        // Current model vocabulary is timesigN (the checked-in model currently contains
        // timesig1, timesig2, timesig4, timesig6, timesig8 and timesig9). Keep the older aliases
        // so previously trained models remain usable. Parsing is generic, so any future timesigN
        // class starts working without another code change.
        foreach (var prefix in new[] { "timesig", "dgt", "digit" })
        {
            if (!className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = className[prefix.Length..];
            if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var digit))
                return digit;
        }

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
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
