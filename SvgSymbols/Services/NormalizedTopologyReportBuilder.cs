using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed class NormalizedTopologyReportBuilder
{
    private static readonly Regex SingleDigit = new(
        @"^(?<family>Music|Bravura-)(?<digit>[0-9])\.svg$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AnyNumber = new(
        @"^(?:Music|Bravura-)(?<value>\d+)\.svg$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SvgShapeNormalizer _normalizer = new();
    private readonly DigitStructuralFeatureExtractor _features = new();
    private readonly NormalizedNumberClassifier _classifier = new();

    public async Task<string> BuildAsync(
        string outputRoot,
        IReadOnlyList<SymbolSource> rhythm,
        CancellationToken cancellationToken = default)
    {
        var rhythmRoot = Path.Combine(outputRoot, "Samples", "Rhythm");
        var normalizedRoot = Path.Combine(rhythmRoot, "normalized");
        Directory.CreateDirectory(normalizedRoot);

        var rows = new List<Row>();

        foreach (var source in rhythm)
        {
            var match = SingleDigit.Match(Path.GetFileName(source.FileName));
            if (!match.Success)
                continue;

            var sourcePath = Path.Combine(rhythmRoot, Path.GetFileName(source.FileName));
            if (!File.Exists(sourcePath))
                continue;

            var normalizedName = Path.GetFileName(source.FileName);
            var normalizedPath = Path.Combine(normalizedRoot, normalizedName);

            try
            {
                _normalizer.NormalizeToFile(sourcePath, normalizedPath);
                rows.Add(new Row(
                    Digit: int.Parse(match.Groups["digit"].Value, CultureInfo.InvariantCulture),
                    Family: source.FileName.StartsWith("Bravura-", StringComparison.OrdinalIgnoreCase)
                        ? "Bravura"
                        : "Wikimedia",
                    FileName: Path.GetFileName(source.FileName),
                    Original: _features.Extract(sourcePath),
                    Normalized: _features.Extract(normalizedPath),
                    Error: null));
            }
            catch (Exception ex)
            {
                rows.Add(new Row(
                    int.Parse(match.Groups["digit"].Value, CultureInfo.InvariantCulture),
                    source.FileName.StartsWith("Bravura-", StringComparison.OrdinalIgnoreCase) ? "Bravura" : "Wikimedia",
                    Path.GetFileName(source.FileName),
                    null,
                    null,
                    ex.Message));
            }
        }

        var model = _classifier.BuildModel(outputRoot, rhythm);
        var classifications = new List<ClassificationRow>();
        foreach (var source in rhythm)
        {
            var fileName = Path.GetFileName(source.FileName);
            var match = AnyNumber.Match(fileName);
            if (!match.Success)
                continue;

            var sourcePath = Path.Combine(rhythmRoot, fileName);
            if (!File.Exists(sourcePath))
                continue;

            var expected = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            var result = _classifier.ClassifySvg(sourcePath, model, fileName);
            classifications.Add(new ClassificationRow(fileName, expected, result));
        }

        var reportPath = Path.Combine(outputRoot, "normalized-topology.html");
        await File.WriteAllTextAsync(reportPath, BuildHtml(rows, classifications), cancellationToken);
        return reportPath;
    }

    private static string BuildHtml(
        IReadOnlyList<Row> rows,
        IReadOnlyList<ClassificationRow> classifications)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Normalized digit topology</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f5f5f5;color:#222}table{border-collapse:collapse;width:100%;background:white;margin-bottom:28px}th,td{border:1px solid #d6d6d6;padding:7px 8px;text-align:left;vertical-align:middle}th{background:#eceff2;position:sticky;top:0}.digit{font-size:22px;font-weight:700;text-align:center}.family{font-weight:700}.glyphs{display:flex;gap:8px;align-items:center}.glyph{width:72px;height:72px;object-fit:contain;background:#fafafa;border:1px solid #eee}.arrow{font-size:22px}.n{font-family:Consolas,monospace;white-space:nowrap}.good{background:#edf8ed}.bad{background:#fff0f0}.error{color:#a00}.result{font-weight:700}.candidates{font-family:Consolas,monospace;font-size:12px;color:#555}.diag{font-family:Consolas,monospace;font-size:12px}</style></head><body>");
        html.AppendLine("<h1>Single digit topology — before / after Skia PathOps</h1>");
        html.AppendLine("<p>Normalized SVGs are written to <code>Samples/Rhythm/normalized</code>. The right-hand image is the union+simplify silhouette used for the second set of measurements. Geometry outside the SVG rendered viewport is clipped before Simplify().</p>");
        html.AppendLine("<table><thead><tr><th>Digit</th><th>Family</th><th>Original → normalized</th><th>Contours raw</th><th>Closed / outer</th><th>Holes</th><th>Aspect</th><th>Fill</th><th>Perimeter</th></tr></thead><tbody>");

        foreach (var group in rows.OrderBy(x => x.Digit).ThenBy(x => x.Family).GroupBy(x => x.Digit))
        {
            var first = true;
            foreach (var row in group)
            {
                html.Append("<tr>");
                if (first)
                {
                    html.Append($"<td class=\"digit\" rowspan=\"{group.Count()}\">{row.Digit}</td>");
                    first = false;
                }

                html.Append($"<td class=\"family\">{WebUtility.HtmlEncode(row.Family)}</td>");
                html.Append("<td><div class=\"glyphs\">" +
                    $"<img class=\"glyph\" src=\"Samples/Rhythm/{Uri.EscapeDataString(row.FileName)}\">" +
                    "<span class=\"arrow\">→</span>" +
                    $"<img class=\"glyph\" src=\"Samples/Rhythm/normalized/{Uri.EscapeDataString(row.FileName)}\">" +
                    "</div></td>");

                if (row.Error is not null || row.Original is null || row.Normalized is null)
                {
                    html.Append($"<td colspan=\"6\" class=\"error\">{WebUtility.HtmlEncode(row.Error ?? "unknown error")}</td></tr>");
                    continue;
                }

                html.Append(Delta(row.Original.RawContourCount, row.Normalized.RawContourCount));
                html.Append(Delta($"{row.Original.ClosedContourCount}/{row.Original.OuterContourCount}", $"{row.Normalized.ClosedContourCount}/{row.Normalized.OuterContourCount}"));
                html.Append(Delta(row.Original.HoleCount, row.Normalized.HoleCount));
                html.Append(Delta(row.Original.AspectRatio, row.Normalized.AspectRatio));
                html.Append(Delta(row.Original.FillRatio, row.Normalized.FillRatio));
                html.Append(Delta(row.Original.NormalizedPerimeter, row.Normalized.NormalizedPerimeter));
                html.AppendLine("</tr>");
            }
        }

        html.AppendLine("</tbody></table>");
        html.AppendLine("<h1>Whole-number classifier — leave one out</h1>");
        html.AppendLine("<p>Whole-shape voting now combines <b>structural</b>, magnitude Fourier and phase-aware <b>complex Fourier</b>: <code>S + 0.15 × Fm + 0.20 × Fc</code>. Green/red is the actual final verdict. Segmented digit-pair hypotheses show dashes for the Fourier diagnostics.</p>");
        html.AppendLine("<table><thead><tr><th>Glyph</th><th>Expected</th><th>Verdict</th><th>Confidence</th><th>Winner S / Fm / Fc / combined</th><th>Top candidates</th></tr></thead><tbody>");

        foreach (var row in classifications.OrderBy(x => x.Expected).ThenBy(x => x.FileName))
        {
            var result = row.Result;
            var correct = result.Value == row.Expected;
            var css = result.Error is not null ? "bad" : correct ? "good" : "bad";
            var winner = result.Candidates.FirstOrDefault();
            var diagnostic = winner is null
                ? "—"
                : winner.StructuralDistance is null || winner.FourierDistance is null || winner.ComplexFourierDistance is null
                    ? $"— / — / — / {winner.Distance:0.000}"
                    : $"{winner.StructuralDistance:0.000} / {winner.FourierDistance:0.000} / {winner.ComplexFourierDistance:0.000} / {winner.Distance:0.000}";

            var candidates = result.Candidates.Count == 0
                ? "—"
                : string.Join(" · ", result.Candidates.Select(FormatCandidate));

            html.AppendLine($"<tr class=\"{css}\">" +
                $"<td><div class=\"glyphs\"><img class=\"glyph\" src=\"Samples/Rhythm/{Uri.EscapeDataString(row.FileName)}\"><span>{WebUtility.HtmlEncode(row.FileName)}</span></div></td>" +
                $"<td class=\"digit\">{row.Expected}</td>" +
                $"<td class=\"result\">{WebUtility.HtmlEncode(result.Value?.ToString(CultureInfo.InvariantCulture) ?? "?")}</td>" +
                $"<td class=\"n\">{result.Confidence * 100:0.0}%</td>" +
                $"<td class=\"diag\">{WebUtility.HtmlEncode(diagnostic)}</td>" +
                $"<td class=\"candidates\">{WebUtility.HtmlEncode(result.Error ?? candidates)}</td>" +
                "</tr>");
        }

        html.AppendLine("</tbody></table></body></html>");
        return html.ToString();
    }

    private static string FormatCandidate(NumberCandidate candidate)
    {
        var parts = candidate.StructuralDistance is null || candidate.FourierDistance is null || candidate.ComplexFourierDistance is null
            ? $"d={candidate.Distance:0.00}"
            : $"S={candidate.StructuralDistance:0.00} Fm={candidate.FourierDistance:0.00} Fc={candidate.ComplexFourierDistance:0.00} C={candidate.Distance:0.00}";

        return $"{candidate.Value}: {candidate.Probability * 100:0.0}% {parts} [{candidate.BestReference}]";
    }

    private static string Delta(int before, int after) =>
        $"<td class=\"n {(after < before ? "good" : string.Empty)}\">{before} → <b>{after}</b></td>";

    private static string Delta(double before, double after) =>
        $"<td class=\"n\">{before:0.000} → <b>{after:0.000}</b></td>";

    private static string Delta(string before, string after) =>
        $"<td class=\"n\">{WebUtility.HtmlEncode(before)} → <b>{WebUtility.HtmlEncode(after)}</b></td>";

    private sealed record Row(
        int Digit,
        string Family,
        string FileName,
        DigitStructuralFeatures? Original,
        DigitStructuralFeatures? Normalized,
        string? Error);

    private sealed record ClassificationRow(
        string FileName,
        int Expected,
        NumberClassification Result);
}
