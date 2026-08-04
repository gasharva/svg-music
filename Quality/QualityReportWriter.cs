using System.Globalization;
using System.Text;

namespace SvgToMusicXmlPoc.Quality;

public sealed class QualityReportWriter
{
    public void Write(QualityComparison comparison, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "quality-report.csv"), ToCsv(comparison), Encoding.UTF8);
        File.WriteAllText(Path.Combine(outputDirectory, "quality-report.md"), ToMarkdown(comparison), Encoding.UTF8);
    }

    public string ToCsv(QualityComparison comparison)
    {
        var builder = new StringBuilder();
        builder.AppendLine("status,part,measure,position,expected,actual,differences,expectedXml,actualXml");
        foreach (var row in comparison.Rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(row.Status),
                Csv(row.Part),
                Csv(row.Measure),
                Csv(row.Position.ToString(CultureInfo.InvariantCulture)),
                Csv(row.Expected),
                Csv(row.Actual),
                Csv(row.Differences),
                Csv(row.ExpectedXml),
                Csv(row.ActualXml)
            }));
        }
        return builder.ToString();
    }

    public string ToMarkdown(QualityComparison comparison)
    {
        var m = comparison.Metrics;
        var builder = new StringBuilder();
        builder.AppendLine("# Golden MusicXML quality report");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| Expected events | {m.Expected} |");
        builder.AppendLine($"| Actual events | {m.Actual} |");
        builder.AppendLine($"| Exact matches | {m.Matched} |");
        builder.AppendLine($"| Mismatches | {m.Mismatched} |");
        builder.AppendLine($"| Missing | {m.Missing} |");
        builder.AppendLine($"| Extra | {m.Extra} |");
        builder.AppendLine($"| Precision | {m.Precision:P2} |");
        builder.AppendLine($"| Recall | {m.Recall:P2} |");
        builder.AppendLine($"| F1 | {m.F1:P2} |");
        builder.AppendLine();
        builder.AppendLine("## Differences");
        builder.AppendLine();
        builder.AppendLine("| Status | Part | Measure | Position | Expected | Actual | Differences |");
        builder.AppendLine("|---|---|---:|---:|---|---|---|");

        foreach (var row in comparison.Rows.Where(x => x.Status != "Matched"))
        {
            builder.AppendLine($"| {Md(row.Status)} | {Md(row.Part)} | {Md(row.Measure)} | {row.Position.ToString(CultureInfo.InvariantCulture)} | {Md(row.Expected)} | {Md(row.Actual)} | {Md(row.Differences)} |");
            if (row.ExpectedXml is not null || row.ActualXml is not null)
            {
                builder.AppendLine();
                builder.AppendLine("<details>");
                builder.AppendLine($"<summary>{Md(row.Status)}: measure {Md(row.Measure)}, position {row.Position.ToString(CultureInfo.InvariantCulture)}</summary>");
                builder.AppendLine();
                if (row.ExpectedXml is not null)
                {
                    builder.AppendLine("**Expected XML**");
                    builder.AppendLine();
                    builder.AppendLine("```xml");
                    builder.AppendLine(row.ExpectedXml);
                    builder.AppendLine("```");
                }
                if (row.ActualXml is not null)
                {
                    builder.AppendLine("**Actual XML**");
                    builder.AppendLine();
                    builder.AppendLine("```xml");
                    builder.AppendLine(row.ActualXml);
                    builder.AppendLine("```");
                }
                builder.AppendLine("</details>");
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string Md(string? value) =>
        (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
