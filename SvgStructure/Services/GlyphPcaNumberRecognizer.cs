using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using GlyphPcaGallery.Models;
using GlyphPcaGallery.Services;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Adapter that lets MeterResolver use the reusable GlyphPcaGallery runtime.
/// The recognizer owns the bundle family name used for time-signature digits.
/// </summary>
public sealed class GlyphPcaNumberRecognizer : ISvgNumberRecognizer
{
    public const string ModelFamily = "meters";

    private readonly GlyphFingerprintAnalyzer _analyzer;
    private readonly string _workDirectory;
    private readonly double _minimumConfidence;

    public GlyphPcaNumberRecognizer(
        GlyphModelBundle bundle,
        string workDirectory,
        double minimumConfidence = 0.20)
        : this(bundle.GetRequired(ModelFamily), workDirectory, minimumConfidence)
    {
    }

    public GlyphPcaNumberRecognizer(
        GlyphModel model,
        string workDirectory,
        double minimumConfidence = 0.20)
    {
        _workDirectory = workDirectory;
        _minimumConfidence = minimumConfidence;
        Directory.CreateDirectory(_workDirectory);
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

        var confidence = 1.0 / (1.0 + Math.Max(0, match.Distance));
        return new SvgNumberCandidate(value.Value, confidence);
    }

    private static int? ParseDigitClass(string className)
    {
        if (int.TryParse(className, NumberStyles.None, CultureInfo.InvariantCulture, out var plainDigit))
            return plainDigit;

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
