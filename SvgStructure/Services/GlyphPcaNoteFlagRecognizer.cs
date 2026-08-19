using System.Numerics;
using System.Text;
using System.Xml.Linq;
using GlyphPcaGallery.Models;
using GlyphPcaGallery.Services;
using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed record NoteFlagCandidate(int Denominator, StemDirection Direction, double Distance, double Confidence);
public sealed record NoteFlagRecognition(int? Denominator, StemDirection? Direction, double Confidence, IReadOnlyList<NoteFlagCandidate> Candidates, string? Error = null);

public sealed class GlyphPcaNoteFlagRecognizer
{
    public const string ModelFamily = "flags";

    private readonly GlyphFingerprintAnalyzer _analyzer;
    private readonly string _workDirectory;

    public GlyphPcaNoteFlagRecognizer(GlyphModelBundle bundle, string workDirectory)
        : this(bundle.GetRequired(ModelFamily), workDirectory)
    {
    }

    public GlyphPcaNoteFlagRecognizer(GlyphModel model, string workDirectory)
    {
        _analyzer = new GlyphFingerprintAnalyzer(model);
        _workDirectory = workDirectory;
        Directory.CreateDirectory(workDirectory);
    }

    public NoteFlagRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new(null, null, 0, Array.Empty<NoteFlagCandidate>(), "No contours supplied.");

        var tempPath = Path.Combine(_workDirectory, $"candidate-{Guid.NewGuid():N}.svg");
        try
        {
            WriteContoursSvg(contours, tempPath);
            var analysis = _analyzer.Analyze(tempPath, Path.GetFileName(tempPath));
            if (analysis is null)
                return new(null, null, 0, Array.Empty<NoteFlagCandidate>(), "Glyph analyzer returned no result.");
            if (analysis.Error is not null)
                return new(null, null, 0, Array.Empty<NoteFlagCandidate>(), analysis.Error);

            var parsed = analysis.Matches
                .Select(x => Parse(x.Class) is { } p
                    ? new NoteFlagCandidate(p.Denominator, p.Direction, x.Distance, 1.0 / (1.0 + Math.Max(0, x.Distance)))
                    : null)
                .Where(x => x is not null)
                .Select(x => x!)
                .ToArray();

            var best = parsed.FirstOrDefault();
            if (!analysis.Accepted || best is null)
                return new(null, null, 0, parsed);

            return new(best.Denominator, best.Direction, best.Confidence, parsed);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static (int Denominator, StemDirection Direction)? Parse(string className)
    {
        var name = className.ToLowerInvariant();
        var direction = name.EndsWith("up", StringComparison.Ordinal) ? StemDirection.Up
            : name.EndsWith("down", StringComparison.Ordinal) ? StemDirection.Down
            : (StemDirection?)null;
        if (direction is null || !name.StartsWith("flag", StringComparison.Ordinal))
            return null;

        var middle = name[4..];
        middle = middle[..^((direction == StemDirection.Up ? "up" : "down").Length)];
        if (!middle.EndsWith("th", StringComparison.Ordinal))
            return null;
        middle = middle[..^2];
        return int.TryParse(middle, out var denominator) && denominator is 8 or 16 or 32
            ? (denominator, direction.Value)
            : null;
    }

    private static void WriteContoursSvg(IReadOnlyList<IReadOnlyList<Vector2>> contours, string outputPath)
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
        new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(outputPath);
    }

    private static string Fmt(float value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
