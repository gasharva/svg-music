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

    public GlyphPcaNumberRecognizer(string modelPath, string workDirectory)
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

            // Rejected open-set classifications must stay rejected. In particular, do not expose
            // weak nearest-class alternatives to MeterResolver: its musical whitelist is allowed to
            // disambiguate accepted digit hypotheses, but must never resurrect geometry that the PCA
            // model has explicitly classified as out-of-distribution / too risky.
            if (!analysis.Accepted)
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
            if (best is null)
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

        // The trained glyph model uses semantic class labels (currently dgt3, dgt4, ...), not
        // bare numeric strings. Keep the adapter responsible for translating model vocabulary into
        // ISvgNumberRecognizer's numeric vocabulary; the PCA project itself remains glyph-agnostic.
        foreach (var prefix in new[] { "dgt", "digit", "timesig" })
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
