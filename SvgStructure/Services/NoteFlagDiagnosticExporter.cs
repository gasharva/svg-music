using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Diagnostic gallery for every symbol candidate that reached NoteFlagResolver's endpoint zone.
/// It intentionally includes candidates rejected before PCA so geometry filters are visible too.
/// </summary>
public sealed class NoteFlagDiagnosticExporter
{
    public string Export(IReadOnlyList<NoteFlagDiagnosticEntry> entries, string outputDirectory)
    {
        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        var ordered = entries
            .OrderBy(x => x.PartNumber)
            .ThenBy(x => x.MeasureNumber)
            .ThenBy(x => x.Stem.PhysicalBounds.CenterX)
            .ThenBy(x => x.Candidate.PhysicalBounds.Left)
            .ToArray();

        var files = new Dictionary<int, string>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var entry = ordered[i];
            var fileName = $"{i + 1:000}-p{entry.PartNumber}-m{entry.MeasureNumber}-c{entry.Candidate.Id}.svg";
            WriteSvg(entry.Candidate, Path.Combine(outputDirectory, fileName));
            files[i] = fileName;
        }

        var indexPath = Path.Combine(outputDirectory, "index.html");
        WriteIndex(indexPath, ordered, files);
        return indexPath;
    }

    private static void WriteIndex(
        string path,
        IReadOnlyList<NoteFlagDiagnosticEntry> entries,
        IReadOnlyDictionary<int, string> files)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>NoteFlagResolver inputs</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}h2{margin-top:34px}table{border-collapse:collapse;width:100%;margin-bottom:24px}th,td{border:1px solid #ddd;padding:7px;vertical-align:middle}th{background:#f2f4f6;text-align:left}.glyph{width:120px;height:90px;object-fit:contain;background:#fff}.mono{font-family:Consolas,monospace;font-size:12px}.ok{background:#eef9ee}.reject{background:#fff5f5}.pre{background:#fff9e8}</style></head><body>");
        html.AppendLine("<h1>NoteFlagResolver inputs</h1>");
        html.AppendLine("<p>Every MusicSymbolCandidate inside the free-stem endpoint search radius is shown, including candidates rejected by geometry before PCA. The SVG is the exact smooth candidate geometry converted to contours for recognition.</p>");

        foreach (var group in entries.GroupBy(x => new { x.PartNumber, x.MeasureNumber }).OrderBy(x => x.Key.PartNumber).ThenBy(x => x.Key.MeasureNumber))
        {
            html.Append($"<h2>Part {group.Key.PartNumber} · Measure {group.Key.MeasureNumber}</h2>");
            html.AppendLine("<table><thead><tr><th>#</th><th>Candidate</th><th>Stem</th><th>Endpoint distance</th><th>PCA candidates</th><th>Verdict</th></tr></thead><tbody>");

            foreach (var entry in group)
            {
                var index = entries.IndexOf(entry);
                var fileName = files[index];
                var css = entry.Verdict.StartsWith("accepted", StringComparison.OrdinalIgnoreCase)
                    ? "ok"
                    : entry.PassedGeometrySanity ? "reject" : "pre";
                var recognition = entry.Recognition;
                var candidates = recognition is null
                    ? "—"
                    : string.Join("<br>", recognition.Candidates.Take(6).Select(x =>
                        $"1/{x.Denominator} {x.Direction}: conf={x.Confidence:0.000} d={x.Distance:0.###}"));

                html.Append($"<tr class=\"{css}\">");
                html.Append($"<td class=\"mono\">{index + 1:000}</td>");
                html.Append($"<td><a href=\"{fileName}\"><img class=\"glyph\" src=\"{fileName}\"></a><br><span class=\"mono\">candidate #{entry.Candidate.Id}</span></td>");
                html.Append($"<td class=\"mono\">{entry.Stem.Direction}<br>x={F(entry.Stem.PhysicalBounds.CenterX)}</td>");
                html.Append($"<td class=\"mono\">{entry.EndpointDistanceInStaffSpaces:0.###} sp</td>");
                html.Append($"<td class=\"mono\">{candidates}</td>");
                html.Append($"<td><b>{WebUtility.HtmlEncode(entry.Verdict)}</b></td>");
                html.AppendLine("</tr>");
            }

            html.AppendLine("</tbody></table>");
        }

        html.AppendLine("</body></html>");
        File.WriteAllText(path, html.ToString());
    }

    private static void WriteSvg(MusicSymbolCandidate candidate, string outputPath)
    {
        var contours = SmoothSymbolContourConverter.ToContours(new[] { candidate });
        var points = contours.SelectMany(x => x).ToArray();
        if (points.Length == 0)
        {
            File.WriteAllText(outputPath, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"/>");
            return;
        }

        var minX = points.Min(x => x.X);
        var minY = points.Min(x => x.Y);
        var maxX = points.Max(x => x.X);
        var maxY = points.Max(x => x.Y);
        var width = Math.Max(1e-6f, maxX - minX);
        var height = Math.Max(1e-6f, maxY - minY);
        var padding = Math.Max(width, height) * 0.08f;

        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(minX - padding)} {F(minY - padding)} {F(width + 2 * padding)} {F(height + 2 * padding)}\">");
        svg.Append($"<rect x=\"{F(minX - padding)}\" y=\"{F(minY - padding)}\" width=\"{F(width + 2 * padding)}\" height=\"{F(height + 2 * padding)}\" fill=\"white\"/>");
        svg.Append("<path fill=\"black\" fill-rule=\"evenodd\" d=\"");
        foreach (var contour in contours.Where(x => x.Count >= 3))
            AppendContour(svg, contour);
        svg.Append("\"/></svg>");
        File.WriteAllText(outputPath, svg.ToString());
    }

    private static void AppendContour(StringBuilder svg, IReadOnlyList<Vector2> contour)
    {
        svg.Append("M ").Append(F(contour[0].X)).Append(' ').Append(F(contour[0].Y));
        for (var i = 1; i < contour.Count; i++)
            svg.Append(" L ").Append(F(contour[i].X)).Append(' ').Append(F(contour[i].Y));
        svg.Append(" Z ");
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal static class NoteFlagDiagnosticListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> source, T value)
    {
        for (var i = 0; i < source.Count; i++)
            if (EqualityComparer<T>.Default.Equals(source[i], value))
                return i;
        return -1;
    }
}
