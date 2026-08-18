using System.Globalization;
using System.Text;
using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed class ArcDiagnosticExporter
{
    public string Export(
        PrimitiveResolution primitives,
        IReadOnlyList<ArcDiagnosticEntry> diagnostics,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var primitiveById = primitives.Primitives.ToDictionary(x => x.Id);
        var rows = diagnostics
            .GroupBy(x => x.PrimitiveId)
            .Select(x => x.Last())
            .OrderByDescending(x => x.Accepted)
            .ThenBy(x => x.Stage)
            .ThenBy(x => x.PhysicalBounds.Top)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ToArray();

        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>ArcResolver diagnostics</title>");
        html.Append("<style>");
        html.Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}h1{margin-bottom:8px}");
        html.Append("table{border-collapse:collapse;width:100%;font-size:13px}th,td{border:1px solid #ddd;padding:8px;vertical-align:top;text-align:left}");
        html.Append("th{position:sticky;top:0;background:#fafafa}.ok{background:#effbef}.reject{background:#fff6f6}");
        html.Append("img.glyph{width:220px;height:100px;object-fit:contain;background:white;border:1px solid #eee}.mono{font-family:Consolas,monospace;font-size:12px}");
        html.Append("</style></head><body>");
        html.Append($"<h1>ArcResolver candidates ({rows.Length})</h1>");
        html.Append("<p>Every primitive considered by ArcResolver is shown with the final rejection/acceptance reason and measured geometry.</p>");
        html.Append("<table><thead><tr><th>#</th><th>Primitive</th><th>Geometry</th><th>Contacts</th><th>Verdict</th></tr></thead><tbody>");

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var fileName = $"arc-{index + 1:000}-id{row.PrimitiveId}.svg";
            var filePath = Path.Combine(outputDirectory, fileName);

            if (primitiveById.TryGetValue(row.PrimitiveId, out var primitive))
                WritePrimitiveSvg(primitive, row, filePath);

            html.Append(row.Accepted ? "<tr class=\"ok\">" : "<tr class=\"reject\">");
            html.Append($"<td>{index + 1}</td>");
            html.Append("<td>");
            if (File.Exists(filePath))
                html.Append($"<a href=\"{fileName}\"><img class=\"glyph\" src=\"{fileName}\"></a><br>");
            html.Append($"<span class=\"mono\">primitive #{row.PrimitiveId}<br>points={row.ContourPointCount}<br>bbox={FormatRect(row.PhysicalBounds)}</span></td>");

            html.Append("<td class=\"mono\">");
            html.Append($"staff={F(row.StaffSpace)}<br>");
            html.Append($"width/staff={F(row.WidthInStaffSpaces)}<br>");
            html.Append($"left thickness={F(row.LeftThicknessInStaffSpaces)}<br>");
            html.Append($"right thickness={F(row.RightThicknessInStaffSpaces)}<br>");
            html.Append($"curvature={F(row.CurvatureInStaffSpaces)}<br>");
            html.Append($"L={FormatPoint(row.LeftEndpoint)}<br>M={FormatPoint(row.Midpoint)}<br>R={FormatPoint(row.RightEndpoint)}");
            html.Append("</td>");

            html.Append("<td class=\"mono\">");
            html.Append($"left nearest={F(row.LeftNearestContactDistanceInStaffSpaces)} staff<br>");
            html.Append($"right nearest={F(row.RightNearestContactDistanceInStaffSpaces)} staff<br>");
            html.Append($"left contacts={row.LeftContactCount}<br>right contacts={row.RightContactCount}");
            html.Append("</td>");

            html.Append($"<td><strong>{Escape(row.Stage)}</strong><br>{Escape(row.Verdict)}</td>");
            html.Append("</tr>");
        }

        html.Append("</tbody></table></body></html>");
        var indexPath = Path.Combine(outputDirectory, "index.html");
        File.WriteAllText(indexPath, html.ToString(), Encoding.UTF8);
        return indexPath;
    }

    private static void WritePrimitiveSvg(
        ResolvedPrimitive primitive,
        ArcDiagnosticEntry diagnostic,
        string path)
    {
        var bounds = primitive.PhysicalBounds;
        var margin = Math.Max(2.0, Math.Max(bounds.Width, bounds.Height) * 0.08);
        var left = bounds.Left - margin;
        var top = bounds.Top - margin;
        var width = Math.Max(1.0, bounds.Width + margin * 2.0);
        var height = Math.Max(1.0, bounds.Height + margin * 2.0);

        var points = primitive.Contour.Points
            .Select(p => $"{p.X.ToString(CultureInfo.InvariantCulture)},{p.Y.ToString(CultureInfo.InvariantCulture)}");

        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(left)} {F(top)} {F(width)} {F(height)}\">\n");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>\n");
        svg.Append($"<polygon points=\"{string.Join(" ", points)}\" fill=\"black\" stroke=\"black\" stroke-width=\"0.2\"/>\n");

        if (diagnostic.LeftEndpoint is { } leftPoint)
            svg.Append(PointCircle(leftPoint, "red"));
        if (diagnostic.Midpoint is { } middlePoint)
            svg.Append(PointCircle(middlePoint, "blue"));
        if (diagnostic.RightEndpoint is { } rightPoint)
            svg.Append(PointCircle(rightPoint, "red"));

        svg.Append("</svg>");
        File.WriteAllText(path, svg.ToString(), Encoding.UTF8);
    }

    private static string PointCircle(PointD point, string color) =>
        $"<circle cx=\"{F(point.X)}\" cy=\"{F(point.Y)}\" r=\"1.2\" fill=\"{color}\"/>\n";

    private static string FormatRect(RectD rect) =>
        $"[{F(rect.Left)},{F(rect.Top)}]-[{F(rect.Right)},{F(rect.Bottom)}]";

    private static string FormatPoint(PointD? point) =>
        point is null ? "-" : $"({F(point.X)},{F(point.Y)})";

    private static string F(double? value) =>
        value is null || double.IsInfinity(value.Value)
            ? "-"
            : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string F(double value) =>
        double.IsInfinity(value) ? "-" : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
