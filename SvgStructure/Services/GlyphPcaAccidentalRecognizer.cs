using System.Numerics;
using System.Text;
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
/// Thin adapter over the glyph-generic PCA runtime. The recognizer owns the bundle family name
/// and translates the model vocabulary into the SvgStructure accidental domain enum.
/// </summary>
public sealed class GlyphPcaAccidentalRecognizer
{
    public const string ModelFamily = "accidentals";

    private readonly GlyphFingerprintAnalyzer _analyzer;
    private readonly string _workDirectory;

    public GlyphPcaAccidentalRecognizer(GlyphModelBundle bundle, string workDirectory)
        : this(bundle.GetRequired(ModelFamily), workDirectory)
    {
    }

    public GlyphPcaAccidentalRecognizer(GlyphModel model, string workDirectory)
    {
        _workDirectory = workDirectory;
        Directory.CreateDirectory(_workDirectory);
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
        "accidentalflat" => AccidentalKind.Flat,
        "accidentalsharp" => AccidentalKind.Sharp,
        "accidentalnatural" => AccidentalKind.Natural,
        "accidentaldoublesharp" => AccidentalKind.DoubleSharp,
        "accidentaldoubleflat" => AccidentalKind.DoubleFlat,
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
