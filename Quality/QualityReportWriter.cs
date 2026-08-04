using System.Globalization;
using System.Text;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Quality;

public sealed class QualityReportWriter
{
    public void Write(QualityComparison comparison, string outputDirectory, ConversionPerformance? performance = null)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "quality-report.csv"), ToCsv(comparison), Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputDirectory, "quality-report.md"), ToMarkdown(comparison, performance), Encoding.UTF8);
        if (performance is not null)
            File.WriteAllText(Path.Combine(outputDirectory, "performance.csv"), ToPerformanceCsv(performance), Encoding.UTF8);
    }

    public string ToCsv(QualityComparison comparison)
    {
        var builder = new StringBuilder();
        builder.AppendLine("status,part,measure,position,expected,actual,differences,expectedXml,actualXml");
        foreach (var row in comparison.Rows)
            builder.AppendLine(string.Join(",", new[] { Csv(row.Status), Csv(row.Part), Csv(row.Measure), Csv(row.Position.ToString(CultureInfo.InvariantCulture)), Csv(row.Expected), Csv(row.Actual), Csv(row.Differences), Csv(row.ExpectedXml), Csv(row.ActualXml) }));
        return builder.ToString();
    }

    public string ToMarkdown(QualityComparison comparison, ConversionPerformance? performance = null)
    {
        var m = comparison.Metrics;
        var builder = new StringBuilder();
        builder.AppendLine("# Golden MusicXML quality report\n");
        builder.AppendLine("## Quality summary\n");
        builder.AppendLine("| Metric | Value |\n|---|---:|");
        builder.AppendLine($"| Expected events | {m.Expected} |");
        builder.AppendLine($"| Actual events | {m.Actual} |");
        builder.AppendLine($"| Exact matches | {m.Matched} |");
        builder.AppendLine($"| Mismatches | {m.Mismatched} |");
        builder.AppendLine($"| Missing | {m.Missing} |");
        builder.AppendLine($"| Extra | {m.Extra} |");
        builder.AppendLine($"| Precision | {m.Precision:P2} |");
        builder.AppendLine($"| Recall | {m.Recall:P2} |");
        builder.AppendLine($"| F1 | {m.F1:P2} |");

        if (performance is not null)
        {
            builder.AppendLine("\n## Performance\n");
            builder.AppendLine("| Stage / counter | Value |\n|---|---:|");
            builder.AppendLine($"| Parse SVG | {performance.ParseSvgMs:F1} ms |");
            builder.AppendLine($"| Detect staves | {performance.DetectStavesMs:F1} ms |");
            builder.AppendLine($"| Read instances | {performance.ReadInstancesMs:F1} ms |");
            builder.AppendLine($"| Load catalog | {performance.LoadCatalogMs:F1} ms |");
            builder.AppendLine($"| Classify | {performance.ClassifyMs:F1} ms |");
            builder.AppendLine($"| Recognize semantics | {performance.RecognizeSemanticsMs:F1} ms |");
            builder.AppendLine($"| Write MusicXML | {performance.WriteMusicXmlMs:F1} ms |");
            builder.AppendLine($"| **Total** | **{performance.TotalMs:F1} ms** |");
            builder.AppendLine($"| Glyph instances | {performance.GlyphInstances} |");
            builder.AppendLine($"| Unique geometries | {performance.UniqueGeometries} |");
            builder.AppendLine($"| Catalog glyphs | {performance.CatalogGlyphs} |");
            builder.AppendLine($"| Bitmap comparisons | {performance.MaskComparisons} |");
            builder.AppendLine($"| Vector comparisons | {performance.VectorComparisons} |");
            builder.AppendLine($"| Catalog cache hit | {performance.CatalogCacheHit} |");
        }

        builder.AppendLine("\n## Differences\n");
        builder.AppendLine("| Status | Part | Measure | Position | Expected | Actual | Differences |\n|---|---|---:|---:|---|---|---|");
        foreach (var row in comparison.Rows.Where(x => x.Status != "Matched"))
        {
            builder.AppendLine($"| {Md(row.Status)} | {Md(row.Part)} | {Md(row.Measure)} | {row.Position.ToString(CultureInfo.InvariantCulture)} | {Md(row.Expected)} | {Md(row.Actual)} | {Md(row.Differences)} |");
            if (row.ExpectedXml is null && row.ActualXml is null) continue;
            builder.AppendLine($"\n<details>\n<summary>{Md(row.Status)}: measure {Md(row.Measure)}, position {row.Position.ToString(CultureInfo.InvariantCulture)}</summary>\n");
            if (row.ExpectedXml is not null) builder.AppendLine($"**Expected XML**\n\n```xml\n{row.ExpectedXml}\n```");
            if (row.ActualXml is not null) builder.AppendLine($"**Actual XML**\n\n```xml\n{row.ActualXml}\n```");
            builder.AppendLine("</details>\n");
        }
        return builder.ToString();
    }

    private static string ToPerformanceCsv(ConversionPerformance p) =>
        "metric,value\n" + string.Join("\n", new[]
        {
            $"parseSvgMs,{F(p.ParseSvgMs)}", $"detectStavesMs,{F(p.DetectStavesMs)}", $"readInstancesMs,{F(p.ReadInstancesMs)}",
            $"loadCatalogMs,{F(p.LoadCatalogMs)}", $"classifyMs,{F(p.ClassifyMs)}", $"recognizeSemanticsMs,{F(p.RecognizeSemanticsMs)}",
            $"writeMusicXmlMs,{F(p.WriteMusicXmlMs)}", $"totalMs,{F(p.TotalMs)}", $"glyphInstances,{p.GlyphInstances}",
            $"uniqueGeometries,{p.UniqueGeometries}", $"catalogGlyphs,{p.CatalogGlyphs}", $"maskComparisons,{p.MaskComparisons}",
            $"vectorComparisons,{p.VectorComparisons}", $"catalogCacheHit,{p.CatalogCacheHit}"
        }) + "\n";

    private static string F(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static string Csv(string? value) { value ??= string.Empty; return $"\"{value.Replace("\"", "\"\"")}\""; }
    private static string Md(string? value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
