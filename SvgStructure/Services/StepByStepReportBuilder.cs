using System.Net;
using System.Text;

namespace SvgStructure.Services;

public sealed class StepByStepReportBuilder
{
    private const string PagesRoot = "https://gasharva.github.io/svg-music/latest/step-by-step";

    public void WriteHtml(string outputPath, IReadOnlyList<StepByStepItemResult> items)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>SvgStructure diagnostics</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f5f5f5;color:#222}table{border-collapse:collapse;width:100%;background:#fff}th,td{border:1px solid #ddd;padding:8px;vertical-align:top}th{background:#eceff2}.preview{width:240px;max-height:340px;object-fit:contain;background:#fff}.good{background:#edf8ed}.bad{background:#fff0f0}.mono{font-family:Consolas,monospace}a{color:#0757a6}</style></head><body>");
        html.AppendLine("<h1>SvgStructure — diagnostics</h1>");
        html.AppendLine("<p>SvgStructure is the resolver library. This report is produced by SvgStructureDiagnostics.</p>");
        html.AppendLine("<table><thead><tr><th>Source</th><th>P+M blocks</th><th>Primitives</th><th>Music symbols</th><th>Semantic symbols</th><th>State</th></tr></thead><tbody>");

        foreach (var item in items)
        {
            var dir = Uri.EscapeDataString(item.ArtifactDirectoryName);
            var css = item.Error is null ? "good" : "bad";
            html.Append($"<tr class=\"{css}\">");
            html.Append($"<td><b>{WebUtility.HtmlEncode(item.FileName)}</b><br><a href=\"{dir}/source.svg\">source.svg</a><br><a href=\"{dir}/source-tree.txt\"><b>Svg.Skia source tree</b></a><br><a href=\"{dir}/source-uses.json\"><b>&lt;use&gt; instances ({item.SourceUseCount})</b></a></td>");

            if (item.Error is not null)
            {
                html.Append($"<td colspan=\"5\"><b>FAILED</b><br>{WebUtility.HtmlEncode(item.Error)}<br><a href=\"{dir}/error.txt\">error.txt</a></td></tr>");
                continue;
            }

            html.Append($"<td><a href=\"{dir}/measures.png\"><img class=\"preview\" src=\"{dir}/measures.png\"></a></td>");
            html.Append($"<td><a href=\"{dir}/classified.png\"><img class=\"preview\" src=\"{dir}/classified.png\"></a><br><a href=\"{dir}/primitives.html\"><b>primitive SVG gallery ({item.ExportedPrimitiveCount})</b></a></td>");
            html.Append($"<td><a href=\"{dir}/music-symbols.html\"><b>MusicSymbol candidates ({item.MusicSymbolCount})</b></a></td>");
            html.Append($"<td><a href=\"{dir}/meters.png\"><img class=\"preview\" src=\"{dir}/meters.png\"></a><br><b>meters: {item.MeterCount}</b><br><b>clefs: {item.ClefCount}</b><br><b>ledger ladders: {item.LedgerLineCount}</b><br><b>note heads: {item.NoteHeadCount}</b><br><b>accidentals: {item.AccidentalCount}</b><br><b>rests: {item.RestCount}</b></td>");

            html.Append("<td class=\"mono\">");
            if (item.ReferenceValidation is not null)
            {
                foreach (var summary in item.ReferenceValidation.Summaries)
                    html.Append($"<b>{WebUtility.HtmlEncode(summary.Resolver)}</b> - {summary.Matched}/{summary.Expected}" + (summary.Extra > 0 ? $" (+{summary.Extra} extra)" : "") + "<br>");
                html.Append($"<a href=\"{dir}/reference-checks.html\"><b>reference checks</b></a><br>");
                html.Append($"<a href=\"{dir}/resolved.musicxml\"><b>resolved.musicxml</b></a><br><br>");
            }
            else
            {
                html.Append("reference: n/a<br><br>");
            }
            html.Append($"source elements: {item.SourceElementCount}<br>source uses: {item.SourceUseCount}<br>lines: {item.LineCount}<br>systems: {item.SystemCount}<br>parts: {item.PartCount}<br>measures: {item.MeasureCount}<br>" +
                        $"P+M primitives: {item.PartMeasurePrimitiveCount}<br>exported SVGs: {item.ExportedPrimitiveCount}<br>M-only: {item.MeasurePrimitiveCount}<br>physical-only: {item.PhysicalOnlyPrimitiveCount}<br>music symbols: {item.MusicSymbolCount}<br>" +
                        $"<a href=\"{dir}/structure.json\">structure.json</a></td>");
            html.AppendLine("</tr>");
        }

        html.AppendLine("</tbody></table></body></html>");
        File.WriteAllText(outputPath, html.ToString());
    }

    public void WriteMarkdown(string outputPath, IReadOnlyList<StepByStepItemResult> items)
    {
        var md = new StringBuilder();
        md.AppendLine("# SvgStructure diagnostics");
        md.AppendLine();
        md.AppendLine($"**[Open the rendered HTML report on GitHub Pages]({PagesRoot}/)**");
        md.AppendLine();
        md.AppendLine("| SVG | P+M blocks | Primitives | Music symbols | Semantic symbols | State |");
        md.AppendLine("|---|---|---|---|---|---|");

        foreach (var item in items)
        {
            var dir = item.ArtifactDirectoryName.Replace(" ", "%20", StringComparison.Ordinal);
            var pageDir = $"{PagesRoot}/{dir}";
            if (item.Error is not null)
            {
                md.AppendLine($"| **{item.FileName}** | FAILED | FAILED | FAILED | FAILED | [error]({pageDir}/error.txt) |");
                continue;
            }

            var state = new StringBuilder();
            if (item.ReferenceValidation is not null)
            {
                foreach (var summary in item.ReferenceValidation.Summaries)
                    state.Append($"{summary.Resolver} - {summary.Matched}/{summary.Expected}" + (summary.Extra > 0 ? $" (+{summary.Extra} extra)" : "") + "<br>");
                state.Append($"[reference checks]({pageDir}/reference-checks.html)<br>[resolved.musicxml]({pageDir}/resolved.musicxml)<br>");
            }
            state.Append($"[json]({pageDir}/structure.json)");

            md.AppendLine(
                $"| **[{item.FileName}]({pageDir}/source.svg)** | " +
                $"[![blocks]({dir}/measures.png)]({pageDir}/measures.png) | " +
                $"[![primitives]({dir}/classified.png)]({pageDir}/classified.png)<br>[gallery]({pageDir}/primitives.html) | " +
                $"[MusicSymbol gallery]({pageDir}/music-symbols.html) ({item.MusicSymbolCount}) | " +
                $"[![symbols]({dir}/meters.png)]({pageDir}/meters.png)<br>clefs={item.ClefCount}<br>noteHeads={item.NoteHeadCount}<br>rests={item.RestCount} | " +
                $"{state} |");
        }

        File.WriteAllText(outputPath, md.ToString());
    }
}
