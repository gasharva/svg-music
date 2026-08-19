using System.Numerics;
using System.Text;
using System.Xml.Linq;
using GlyphPcaGallery.Models;
using GlyphPcaGallery.Services;

namespace SvgStructure.Services;

public sealed record RestCandidate(int Denominator, double Distance, double Confidence, string ClassName);
public sealed record RestRecognition(int? Denominator, double Confidence, IReadOnlyList<RestCandidate> Candidates, string? Error = null);

/// <summary>Thin adapter over the glyph-generic PCA runtime for the dedicated rests family.</summary>
public sealed class GlyphPcaRestRecognizer
{
    public const string ModelFamily = "rests";

    private readonly GlyphFingerprintAnalyzer _analyzer;
    private readonly string _workDirectory;

    public GlyphPcaRestRecognizer(GlyphModelBundle bundle, string workDirectory)
        : this(bundle.GetRequired(ModelFamily), workDirectory)
    {
    }

    public GlyphPcaRestRecognizer(GlyphModel model, string workDirectory)
    {
        _analyzer = new GlyphFingerprintAnalyzer(model);
        _workDirectory = workDirectory;
        Directory.CreateDirectory(workDirectory);
    }

    public RestRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new(null, 0, Array.Empty<RestCandidate>(), "No contours supplied.");

        var tempPath = Path.Combine(_workDirectory, $"candidate-{Guid.NewGuid():N}.svg");
        try
        {
            WriteContoursSvg(contours, tempPath);
            var analysis = _analyzer.Analyze(tempPath, Path.GetFileName(tempPath));
            if (analysis is null)
                return new(null, 0, Array.Empty<RestCandidate>(), "Glyph analyzer returned no result.");
            if (analysis.Error is not null)
                return new(null, 0, Array.Empty<RestCandidate>(), analysis.Error);

            var parsed = analysis.Matches
                .Select(ToCandidate)
                .Where(x => x is not null)
                .Select(x => x!)
                .GroupBy(x => x.Denominator)
                .Select(x => x.OrderBy(c => c.Distance).First())
                .OrderBy(x => x.Distance)
                .ToArray();

            var best = parsed.FirstOrDefault();
            if (!analysis.Accepted || best is null)
                return new(null, 0, parsed);

            return new(best.Denominator, best.Confidence, parsed);
        }
        catch (Exception ex)
        {
            return new(null, 0, Array.Empty<RestCandidate>(), ex.Message);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static RestCandidate? ToCandidate(ClassMatch match)
    {
        var denominator = ParseClass(match.Class);
        if (denominator is null)
            return null;
        return new RestCandidate(
            denominator.Value,
            match.Distance,
            1.0 / (1.0 + Math.Max(0, match.Distance)),
            match.Class);
    }

    private static int? ParseClass(string className)
    {
        var name = className.ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        if (name.StartsWith("rest", StringComparison.Ordinal))
            name = name[4..];

        if (name.EndsWith("rest", StringComparison.Ordinal))
            name = name[..^4];

        return name switch
        {
            "maxima" or "longa" or "long" => 0,
            "breve" or "doublewhole" => 0,
            "whole" or "semibreve" or "1" => 1,
            "half" or "minim" or "2" => 2,
            "quarter" or "crotchet" or "4" => 4,
            "eighth" or "8th" or "quaver" or "8" => 8,
            "sixteenth" or "16th" or "semiquaver" or "16" => 16,
            "thirtysecond" or "32nd" or "demisemiquaver" or "32" => 32,
            "sixtyfourth" or "64th" or "hemidemisemiquaver" or "64" => 64,
            "128th" or "128" => 128,
            _ => null
        };
    }

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
