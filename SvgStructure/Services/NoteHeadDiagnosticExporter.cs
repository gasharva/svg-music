using System.Globalization;
using System.Net;
using System.Text;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Writes the exact geometric candidates inspected by NoteHeadResolver, grouped by staff and measure.
/// This is diagnostic output only; it does not participate in recognition.
/// </summary>
public sealed class NoteHeadDiagnosticExporter
{
    public string Export(
        IReadOnlyList<NoteHeadDiagnosticEntry> entries,
        string outputDirectory)
    {
        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        var ordered = entries
            .OrderBy(x => x.PartNumber)
            .ThenBy(x => x.MeasureNumber)
            .ThenBy(x => LogicalCenterX(x.LogicalBounds) ?? double.MinValue)
            .ThenBy(x => x.LogicalBounds.Top)
            .ToArray();

        var fileByPrimitive = new Dictionary<int, string>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var entry = ordered[i];
            var stem = $"p{entry.PartNumber}-m{entry.MeasureNumber}-{i + 1:000}-id{entry.PrimitiveId}";
            var fileName = stem + ".svg";
            WriteSvg(entry, Path.Combine(outputDirectory, fileName));
            fileByPrimitive[entry.PrimitiveId] = fileName;
        }

        var indexPath = Path.Combine(outputDirectory, "index.html");
        WriteIndex(indexPath, ordered, fileByPrimitive);
        return indexPath;
    }

    private static void WriteIndex(
        string path,
        IReadOnlyList<NoteHeadDiagnosticEntry> entries,
        IReadOnlyDictionary<int, string> fileByPrimitive)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>NoteHeadResolver diagnostics</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}h2{margin-top:34px}table{border-collapse:collapse;width:100%;margin-bottom:24px}th,td{border:1px solid #ddd;padding:7px;vertical-align:middle}th{background:#f2f4f6;text-align:left}.glyph{width:120px;height:80px;object-fit:contain;background:white}.mono{font-family:Consolas,monospace;font-size:12px}.ok{background:#eef9ee}.reject{background:#fff5f5}.hollow{font-weight:700;color:#7a3b00}</style></head><body>");
        html.AppendLine("<h1>NoteHeadResolver candidates</h1>");
        html.AppendLine("<p>Only candidates that passed the basic small/smooth/oval pre-filter are shown. Rows are grouped by staff (part) and measure, and ordered left-to-right. The image contains the source contour group used by the hollow/filled test; the detected outer candidate is outlined in green.</p>");

        var groups = entries
            .GroupBy(x => new { x.PartNumber, x.MeasureNumber })
            .OrderBy(x => x.Key.PartNumber)
            .ThenBy(x => x.Key.MeasureNumber);

        foreach (var group in groups)
        {
            html.Append($"<h2>Part {group.Key.PartNumber} · Measure {group.Key.MeasureNumber}</h2>");
            html.AppendLine("<table><thead><tr><th>#</th><th>Candidate</th><th>Logical position</th><th>Source contours</th><th>Hollow test</th><th>Verdict</th></tr></thead><tbody>");

            var sequence = 0;
            foreach (var entry in group)
            {
                sequence++;
                var css = entry.Accepted ? "ok" : "reject";
                var fileName = fileByPrimitive[entry.PrimitiveId];
                var centerX = LogicalCenterX(entry.LogicalBounds);
                var centerY = (entry.LogicalBounds.Top + entry.LogicalBounds.Bottom) / 2.0;
                var sourceContourCount = entry.SourceGroupContours?.Count ?? 0;
                var hollow = entry.HollowContourDetected ? "YES" : "no";
                var hollowCss = entry.HollowContourDetected ? "hollow" : string.Empty;

                html.Append($"<tr class=\"{css}\">");
                html.Append($"<td>{sequence}</td>");
                html.Append($"<td><a href=\"{fileName}\"><img class=\"glyph\" src=\"{fileName}\"></a><br><span class=\"mono\">primitive #{entry.PrimitiveId}</span></td>");
                html.Append($"<td class=\"mono\">x={F(centerX)}<br>y={F(centerY)}<br>bbox={Html(LogicalBoundsText(entry.LogicalBounds))}</td>");
                html.Append($"<td>{sourceContourCount}</td>");
                html.Append($"<td class=\"{hollowCss}\">{hollow}</td>");
                html.Append($"<td><b>{Html(entry.Verdict)}</b></td>");
                html.AppendLine("</tr>");
            }

            html.AppendLine("</tbody></table>");
        }

        html.AppendLine("</body></html>");
        File.WriteAllText(path, html.ToString());
    }

    private static void WriteSvg(NoteHeadDiagnosticEntry entry, string path)
    {
        var contours = entry.SourceGroupContours is { Count: > 0 }
            ? entry.SourceGroupContours
            : new[] { entry.Contour };

        var allPoints = contours
            .SelectMany(x => x.Points)
            .ToArray();

        if (allPoints.Length == 0)
        {
            File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"/>");
            return;
        }

        var minX = allPoints.Min(x => (double)x.X);
        var minY = allPoints.Min(x => (double)x.Y);
        var maxX = allPoints.Max(x => (double)x.X);
        var maxY = allPoints.Max(x => (double)x.Y);
        var width = Math.Max(1e-6, maxX - minX);
        var height = Math.Max(1e-6, maxY - minY);
        var padding = Math.Max(width, height) * 0.10;

        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(minX - padding)} {F(minY - padding)} {F(width + 2 * padding)} {F(height + 2 * padding)}\">");
        svg.Append("<rect x=\"").Append(F(minX - padding)).Append("\" y=\"").Append(F(minY - padding))
            .Append("\" width=\"").Append(F(width + 2 * padding)).Append("\" height=\"").Append(F(height + 2 * padding)).Append("\" fill=\"white\"/>");

        svg.Append("<path fill=\"black\" fill-rule=\"evenodd\" d=\"");
        foreach (var contour in contours.Where(x => x.Points.Count >= 3))
            AppendContour(svg, contour);
        svg.Append("\"/>");

        if (entry.Contour.Points.Count >= 3)
        {
            svg.Append("<path fill=\"none\" stroke=\"#16843a\" stroke-width=\"")
                .Append(F(Math.Max(width, height) * 0.015))
                .Append("\" d=\"");
            AppendContour(svg, entry.Contour);
            svg.Append("\"/>");
        }

        svg.Append("</svg>");
        File.WriteAllText(path, svg.ToString());
    }

    private static void AppendContour(StringBuilder svg, PrimitiveContour contour)
    {
        if (contour.Points.Count == 0)
            return;

        svg.Append("M ").Append(F(contour.Points[0].X)).Append(' ').Append(F(contour.Points[0].Y));
        for (var i = 1; i < contour.Points.Count; i++)
            svg.Append(" L ").Append(F(contour.Points[i].X)).Append(' ').Append(F(contour.Points[i].Y));
        svg.Append(" Z ");
    }

    private static string LogicalBoundsText(LogicalRectD b) =>
        $"[{F(b.Left)},{F(b.Top)}]-[{F(b.Right)},{F(b.Bottom)}]";

    private static double? LogicalCenterX(LogicalRectD bounds) =>
        bounds.Left is { } left && bounds.Right is { } right ? (left + right) / 2.0 : null;

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string F(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null";

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
