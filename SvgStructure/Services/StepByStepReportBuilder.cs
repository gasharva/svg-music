using System.Net;
using System.Text;

namespace SvgStructure.Services;

public sealed class StepByStepReportBuilder
{
    public void WriteHtml(string outputPath, IReadOnlyList<StepByStepItemResult> items)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>SvgStructure step-by-step</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f5f5f5;color:#222}table{border-collapse:collapse;width:100%;background:#fff}th,td{border:1px solid #ddd;padding:8px;vertical-align:top}th{background:#eceff2}.preview{width:280px;max-height:360px;object-fit:contain;background:#fff}.good{background:#edf8ed}.bad{background:#fff0f0}.mono{font-family:Consolas,monospace}a{color:#0757a6}</style></head><body>");
        html.AppendLine("<h1>SvgStructure — step-by-step</h1>");
        html.AppendLine("<p><b>Step 1 — PartMeasureResolver:</b> SVG → parts + measures + logical/physical coordinate map.<br><b>Step 2 — PrimitiveResolver:</b> raw primitives → Pn-Mm, measure-only, or physical-only ownership. Overlays below are diagnostics only.</p>");
        html.AppendLine("<table><thead><tr><th>Source</th><th>Step 1: Part + Measure map</th><th>Step 2: Primitive ownership</th><th>Resolved data</th></tr></thead><tbody>");

        foreach (var item in items)
        {
            var dir = Uri.EscapeDataString(item.ArtifactDirectoryName);
            var css = item.Error is null ? "good" : "bad";
            html.Append($"<tr class=\"{css}\">");
            html.Append($"<td><b>{WebUtility.HtmlEncode(item.FileName)}</b><br><a href=\"{dir}/source.svg\">source.svg</a></td>");

            if (item.Error is not null)
            {
                html.Append($"<td colspan=\"3\"><b>FAILED</b><br>{WebUtility.HtmlEncode(item.Error)}<br><a href=\"{dir}/error.txt\">error.txt</a></td></tr>");
                continue;
            }

            html.Append($"<td><a href=\"{dir}/measures.png\"><img class=\"preview\" src=\"{dir}/measures.png\"></a></td>");
            html.Append($"<td><a href=\"{dir}/classified.png\"><img class=\"preview\" src=\"{dir}/classified.png\"></a></td>");
            html.Append("<td class=\"mono\">" +
                        $"lines: {item.LineCount}<br>systems: {item.SystemCount}<br>parts: {item.PartCount}<br>measures: {item.MeasureCount}<br><br>" +
                        $"P+M primitives: {item.PartMeasurePrimitiveCount}<br>measure-only: {item.MeasurePrimitiveCount}<br>physical-only: {item.PhysicalOnlyPrimitiveCount}<br><br>" +
                        $"<a href=\"{dir}/structure.json\">structure.json</a></td>");
            html.AppendLine("</tr>");
        }

        html.AppendLine("</tbody></table></body></html>");
        File.WriteAllText(outputPath, html.ToString());
    }

    public void WriteMarkdown(string outputPath, IReadOnlyList<StepByStepItemResult> items)
    {
        var md = new StringBuilder();
        md.AppendLine("# SvgStructure step-by-step");
        md.AppendLine();
        md.AppendLine("**Step 1 — PartMeasureResolver:** SVG → parts + measures + logical/physical coordinate map.  ");
        md.AppendLine("**Step 2 — PrimitiveResolver:** primitives → Pn-Mm, measure-only, or physical-only ownership.  ");
        md.AppendLine("Overlays are diagnostics only.");
        md.AppendLine();
        md.AppendLine("| SVG | Step 1 | Step 2 | Resolved data |");
        md.AppendLine("|---|---|---|---|");

        foreach (var item in items)
        {
            var dir = item.ArtifactDirectoryName.Replace(" ", "%20", StringComparison.Ordinal);
            if (item.Error is not null)
            {
                md.AppendLine($"| **{item.FileName}** | FAILED | FAILED | [error]({dir}/error.txt) |");
                continue;
            }

            md.AppendLine(
                $"| **[{item.FileName}]({dir}/source.svg)** | " +
                $"[![part-measure map]({dir}/measures.png)]({dir}/measures.png) | " +
                $"[![primitive ownership]({dir}/classified.png)]({dir}/classified.png) | " +
                $"parts={item.PartCount}<br>measures={item.MeasureCount}<br>P+M={item.PartMeasurePrimitiveCount}<br>M-only={item.MeasurePrimitiveCount}<br>physical={item.PhysicalOnlyPrimitiveCount}<br>[json]({dir}/structure.json) |");
        }

        md.AppendLine();
        md.AppendLine("A standalone browser report is also available as [index.html](index.html).");
        File.WriteAllText(outputPath, md.ToString());
    }
}
