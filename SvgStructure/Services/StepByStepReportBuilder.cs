using System.Net;
using System.Text;

namespace SvgStructure.Services;

public sealed class StepByStepReportBuilder
{
    public void WriteHtml(string outputPath, IReadOnlyList<StepByStepItemResult> items)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>SvgStructure step-by-step</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f5f5f5;color:#222}table{border-collapse:collapse;width:100%;background:#fff}th,td{border:1px solid #ddd;padding:8px;vertical-align:top}th{background:#eceff2}.preview{width:240px;max-height:340px;object-fit:contain;background:#fff}.good{background:#edf8ed}.bad{background:#fff0f0}.mono{font-family:Consolas,monospace}a{color:#0757a6}</style></head><body>");
        html.AppendLine("<h1>SvgStructure — step-by-step</h1>");
        html.AppendLine("<p>PartMeasureResolver → PrimitiveResolver → MusicSymbolResolver → MeterResolver → logical grid → ClefResolver → LedgerLineResolver. Overlays are diagnostics only.</p>");
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
            html.Append($"<td><a href=\"{dir}/music-symbols.html\"><b>MusicSymbol candidates ({item.MusicSymbolCount})</b></a><br>strict overlap grouping<br>original Bezier paths</td>");
            html.Append($"<td><a href=\"{dir}/meters.png\"><img class=\"preview\" src=\"{dir}/meters.png\"></a><br><b>meters: {item.MeterCount}</b><br><b>clefs: {item.ClefCount}</b><br><b>ledger ladders: {item.LedgerLineCount}</b><br><a href=\"{dir}/meter-inputs/index.html\"><b>meter inputs</b></a><br><a href=\"{dir}/clef-inputs/README.md\"><b>clef inputs</b></a></td>");
            html.Append("<td class=\"mono\">" +
                        $"source elements: {item.SourceElementCount}<br>source uses: {item.SourceUseCount}<br>lines: {item.LineCount}<br>systems: {item.SystemCount}<br>parts: {item.PartCount}<br>measures: {item.MeasureCount}<br>" +
                        $"P+M primitives: {item.PartMeasurePrimitiveCount}<br>exported SVGs: {item.ExportedPrimitiveCount}<br>M-only: {item.MeasurePrimitiveCount}<br>physical-only: {item.PhysicalOnlyPrimitiveCount}<br>music symbols: {item.MusicSymbolCount}<br>meters: {item.MeterCount}<br>clefs: {item.ClefCount}<br>ledger ladders: {item.LedgerLineCount}<br>" +
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
        md.AppendLine("Latest results: PartMeasureResolver → PrimitiveResolver → MusicSymbolResolver → MeterResolver → logical grid → ClefResolver → LedgerLineResolver.");
        md.AppendLine();
        md.AppendLine("| SVG | P+M blocks | Primitives | Music symbols | Semantic symbols | State |");
        md.AppendLine("|---|---|---|---|---|---|");

        foreach (var item in items)
        {
            var dir = item.ArtifactDirectoryName.Replace(" ", "%20", StringComparison.Ordinal);
            if (item.Error is not null)
            {
                md.AppendLine($"| **{item.FileName}** | FAILED | FAILED | FAILED | FAILED | [error]({dir}/error.txt) |");
                continue;
            }

            md.AppendLine(
                $"| **[{item.FileName}]({dir}/source.svg)**<br>[source tree]({dir}/source-tree.txt)<br>[uses={item.SourceUseCount}]({dir}/source-uses.json) | " +
                $"[![blocks]({dir}/measures.png)]({dir}/measures.png) | " +
                $"[![primitives]({dir}/classified.png)]({dir}/classified.png)<br>[primitive SVG gallery]({dir}/primitives.html) ({item.ExportedPrimitiveCount}) | " +
                $"[MusicSymbol gallery]({dir}/music-symbols.html) ({item.MusicSymbolCount}) | " +
                $"[![symbols]({dir}/meters.png)]({dir}/meters.png)<br>meters={item.MeterCount}<br>clefs={item.ClefCount}<br>ledgerLadders={item.LedgerLineCount}<br>[meter inputs]({dir}/meter-inputs/index.html)<br>[clef inputs]({dir}/clef-inputs/README.md) | " +
                $"sourceElements={item.SourceElementCount}<br>sourceUses={item.SourceUseCount}<br>parts={item.PartCount}<br>measures={item.MeasureCount}<br>P+M={item.PartMeasurePrimitiveCount}<br>exported={item.ExportedPrimitiveCount}<br>M-only={item.MeasurePrimitiveCount}<br>physical={item.PhysicalOnlyPrimitiveCount}<br>musicSymbols={item.MusicSymbolCount}<br>[json]({dir}/structure.json) |");
        }

        md.AppendLine();
        md.AppendLine("A standalone browser report is also available as [index.html](index.html).");
        File.WriteAllText(outputPath, md.ToString());
    }
}
