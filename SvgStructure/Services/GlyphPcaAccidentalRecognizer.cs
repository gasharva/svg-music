using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GlyphPcaGallery.Models;
using GlyphPcaGallery.Services;
using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed record AccidentalCandidate(AccidentalKind Kind, double Distance, double Confidence);
public sealed record AccidentalRecognition(
    AccidentalKind? Kind,
    double Confidence,
    IReadOnlyList<AccidentalCandidate> Candidates,
    string? Error = null);

/// <summary>
/// Thin adapter over the glyph-generic PCA model. At the moment the trained model contains
/// flat and sharp. Natural/double-sharp/double-flat are deliberately present in the domain enum
/// and can be enabled simply by adding corresponding PCA classes later.
/// </summary>
public sealed class GlyphPcaAccidentalRecognizer
{
    private readonly GlyphFingerprintAnalyzer _analyzer;
    private readonly string _workDirectory;

    public GlyphPcaAccidentalRecognizer(string modelPath, string workDirectory)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Glyph PCA model not found.", modelPath);

        _workDirectory = workDirectory;
        Directory.CreateDirectory(_workDirectory);

        var model = JsonSerializer.Deserialize<GlyphModel>(
            File.ReadAllText(modelPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Could not deserialize glyph PCA model.");
        _analyzer = new GlyphFingerprintAnalyzer(model);
    }

    public AccidentalRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new AccidentalRecognition(null, 0, Array.Empty<AccidentalCandidate>(), "No contours supplied.");

        var tempPath = Path.Combine(_workDirectory, $"candidate-{Guid.NewGuid():N}.svg");
        try
        {
            WriteContoursSvg(contours, tempPath);
            var analysis = _analyzer.Analyze(tempPath, Path.GetFileName(tempPath));
            if (analysis is null)
                return new AccidentalRecognition(null, 0, Array.Empty<AccidentalCandidate>(), "Glyph analyzer returned no result.");
            if (analysis.Error is not null)
                return new AccidentalRecognition(null, 0, Array.Empty<AccidentalCandidate>(), analysis.Error);

            // Never resurrect a lower-ranked accidental if the global PCA verdict was another class.
            var globalBest = analysis.Matches.FirstOrDefault();
            var globalKind = globalBest is null ? null : ParseClass(globalBest.Class);
            if (!analysis.Accepted || globalKind is null)
                return new AccidentalRecognition(null, 0, Array.Empty<AccidentalCandidate>());

            var candidates = analysis.Matches
                .Select(ToCandidate)
                .Where(x => x is not null)
                .Select(x => x!)
                .GroupBy(x => x.Kind)
                .Select(x => x.OrderBy(c => c.Distance).First())
                .OrderBy(x => x.Distance)
                .ToArray();

            var best = candidates.FirstOrDefault();
            return best is null
                ? new AccidentalRecognition(null, 0, Array.Empty<AccidentalCandidate>())
                : new AccidentalRecognition(best.Kind, best.Confidence, candidates);
        }
        catch (Exception ex)
        {
            return new AccidentalRecognition(null, 0, Array.Empty<AccidentalCandidate>(), ex.Message);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static AccidentalCandidate? ToCandidate(ClassMatch match)
    {
        var kind = ParseClass(match.Class);
        if (kind is null)
            return null;
        return new AccidentalCandidate(kind.Value, match.Distance, 1.0 / (1.0 + Math.Max(0, match.Distance)));
    }

    private static AccidentalKind? ParseClass(string name) => name.ToLowerInvariant() switch
    {
        "flat" => AccidentalKind.Flat,
        "sharp" => AccidentalKind.Sharp,
        "natural" => AccidentalKind.Natural,
        "double-sharp" or "doublesharp" => AccidentalKind.DoubleSharp,
        "double-flat" or "doubleflat" => AccidentalKind.DoubleFlat,
        _ => null
    };

    private static void WriteContoursSvg(IReadOnlyList<IReadOnlyList<Vector2>> contours, string path)
    {
        var usable = contours.Where(x => x.Count >= 3).ToArray();
        if (usable.Length == 0)
            throw new InvalidOperationException("No usable contours supplied.");

        var points = usable.SelectMany(x => x).ToArray();
        var minX = points.Min(x => x.X); var minY = points.Min(x => x.Y);
        var maxX = points.Max(x => x.X); var maxY = points.Max(x => x.Y);
        var width = Math.Max(1e-6f, maxX - minX); var height = Math.Max(1e-6f, maxY - minY);
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
            new XAttribute("viewBox", $"{Fmt(minX-padding)} {Fmt(minY-padding)} {Fmt(width+2*padding)} {Fmt(height+2*padding)}"),
            new XElement(ns + "path", new XAttribute("d", d.ToString()), new XAttribute("fill", "black"), new XAttribute("fill-rule", "evenodd")));
        new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(path);
    }

    private static string Fmt(float value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
