using System.Globalization;
using System.Net;
using System.Text;

namespace SvgSymbols.Services;

/// <summary>
/// Detailed laboratory report for real clef candidates exported by SvgStructure.
/// It deliberately reproduces BravuraClefRecognizer's current scoring formula while also
/// exposing the raw distances and vector descriptor data that formula hides.
/// </summary>
public sealed class ClefDiagnosticReportBuilder
{
    private readonly SvgShapeNormalizer _normalizer = new();
    private readonly FourierDescriptorAnalyzer _fourier = new();
    private readonly FourierDescriptorComparer _comparer = new();

    public string Build(string outputRoot, string candidatesRoot, string referenceGlyphDirectory)
    {
        var outputPath = Path.Combine(outputRoot, "clef-diagnostics.html");
        var assetsRoot = Path.Combine(outputRoot, "clef-diagnostics-assets");

        if (Directory.Exists(assetsRoot))
            Directory.Delete(assetsRoot, recursive: true);
        Directory.CreateDirectory(assetsRoot);

        var references = new[]
        {
            BuildReference("G", Path.Combine(referenceGlyphDirectory, "gClef.svg"), assetsRoot),
            BuildReference("F", Path.Combine(referenceGlyphDirectory, "fClef.svg"), assetsRoot)
        };

        var samples = Directory.Exists(candidatesRoot)
            ? Directory.EnumerateFiles(candidatesRoot, "*.svg", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        var rows = samples.Select((path, index) => AnalyzeSample(
            path,
            index + 1,
            outputRoot,
            candidatesRoot,
            assetsRoot,
            references)).ToArray();

        var gfComplex = _comparer.ComplexDistance(references[0].Descriptor, references[1].Descriptor);
        var gfMagnitude = _comparer.MagnitudeDistance(references[0].Descriptor, references[1].Descriptor);

        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Real clef diagnostics</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f5f5f5;color:#222}table{border-collapse:collapse;width:100%;background:#fff}th,td{border:1px solid #ddd;padding:7px;vertical-align:top}th{background:#eceff2;position:sticky;top:0}.img{width:120px;height:120px;object-fit:contain;background:#fff}.mono{font-family:Consolas,monospace;font-size:12px}.weak{background:#fff1f1}.ok{background:#eef8ee}.winner{font-weight:700}.tiny{font-size:11px;color:#555}details{max-width:520px}</style></head><body>");
        html.AppendLine("<h1>Real clef diagnostics</h1>");
        html.AppendLine("<p>Every row is an exact post-sanity-filter candidate exported by SvgStructure. Distances are computed after the same SvgShapeNormalizer used by BravuraClefRecognizer.</p>");
        html.AppendLine($"<p><b>Reference separation:</b> G↔F complex={gfComplex:0.###}, magnitude={gfMagnitude:0.###}. Current production score is <code>complex + 0.20*magnitude</code>, then softmax temperature 1.6 and absolute quality <code>exp(-minDistance/6)</code>.</p>");
        html.AppendLine("<table><thead><tr><th>Source</th><th>Raw</th><th>Normalized</th><th>Descriptor</th><th>G distances</th><th>F distances</th><th>Current verdict</th><th>Deep descriptor dump</th></tr></thead><tbody>");

        foreach (var row in rows)
        {
            var css = row.Confidence >= 0.16 ? "ok" : "weak";
            html.Append($"<tr class=\"{css}\">");
            html.Append($"<td class=\"mono\"><b>{WebUtility.HtmlEncode(row.SourceLabel)}</b><br>{WebUtility.HtmlEncode(row.Context)}</td>");
            html.Append($"<td><a href=\"{row.RawSvg}\"><img class=\"img\" src=\"{row.RawPng}\"></a></td>");
            html.Append($"<td><a href=\"{row.NormalizedSvg}\"><img class=\"img\" src=\"{row.NormalizedSvg}\"></a></td>");
            html.Append($"<td class=\"mono\">rawContours={row.Descriptor.RawContourCount}<br>contours={row.Descriptor.ContourCount}<br>H={FormatInts(row.Descriptor.Scanlines.HorizontalIntersections)}<br>V={FormatInts(row.Descriptor.Scanlines.VerticalIntersections)}</td>");
            html.Append($"<td class=\"mono\">complex={row.G.Complex:0.###}<br>magnitude={row.G.Magnitude:0.###}<br><b>combined={row.G.Combined:0.###}</b></td>");
            html.Append($"<td class=\"mono\">complex={row.F.Complex:0.###}<br>magnitude={row.F.Magnitude:0.###}<br><b>combined={row.F.Combined:0.###}</b></td>");
            html.Append($"<td class=\"mono\"><span class=\"winner\">{row.Winner}</span><br>confidence={row.Confidence:P2}<br>absoluteQuality={row.AbsoluteQuality:P2}<br>distance margin={row.Margin:0.###}<br>softmax share={row.SoftmaxShare:P2}</td>");
            html.Append($"<td>{BuildDetails(row.Descriptor)}</td></tr>");
        }

        html.AppendLine("</tbody></table></body></html>");
        File.WriteAllText(outputPath, html.ToString());
        return outputPath;
    }

    private Reference BuildReference(string symbol, string sourcePath, string assetsRoot)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Clef reference not found.", sourcePath);

        var normalized = Path.Combine(assetsRoot, $"reference-{symbol}.svg");
        _normalizer.NormalizeToFile(sourcePath, normalized);
        return new Reference(symbol, _fourier.Analyze(normalized));
    }

    private Row AnalyzeSample(
        string path,
        int index,
        string outputRoot,
        string candidatesRoot,
        string assetsRoot,
        IReadOnlyList<Reference> references)
    {
        var relative = Path.GetRelativePath(candidatesRoot, path).Replace('\\', '/');
        var safeName = $"{index:000}-{Path.GetFileNameWithoutExtension(path)}.normalized.svg";
        var normalizedPath = Path.Combine(assetsRoot, safeName);
        _normalizer.NormalizeToFile(path, normalizedPath);
        var descriptor = _fourier.Analyze(normalizedPath);

        var distances = references.Select(reference =>
        {
            var complex = _comparer.ComplexDistance(descriptor, reference.Descriptor);
            var magnitude = _comparer.MagnitudeDistance(descriptor, reference.Descriptor);
            return new Distance(reference.Symbol, complex, magnitude, complex + 0.20 * magnitude);
        }).OrderBy(x => x.Combined).ToArray();

        var min = distances[0].Combined;
        const double temperature = 1.6;
        var weights = distances.Select(x => Math.Exp(-(x.Combined - min) / temperature)).ToArray();
        var weightTotal = Math.Max(1e-12, weights.Sum());
        var softmaxShare = weights[0] / weightTotal;
        var absoluteQuality = Math.Exp(-min / 6.0);
        var confidence = Math.Clamp(softmaxShare * absoluteQuality, 0d, 1d);
        var margin = distances.Length > 1 ? distances[1].Combined - distances[0].Combined : 0d;

        var g = distances.Single(x => x.Symbol == "G");
        var f = distances.Single(x => x.Symbol == "F");
        var txtPath = Path.ChangeExtension(path, ".txt");
        var context = File.Exists(txtPath)
            ? string.Join(" | ", File.ReadLines(txtPath).Take(2))
            : string.Empty;

        var rawSvg = RelativeUrl(outputRoot, path);
        var rawPngPath = Path.ChangeExtension(path, ".png");
        var rawPng = File.Exists(rawPngPath) ? RelativeUrl(outputRoot, rawPngPath) : rawSvg;
        var normalizedSvg = RelativeUrl(outputRoot, normalizedPath);

        return new Row(relative, context, rawSvg, rawPng, normalizedSvg, descriptor, g, f,
            distances[0].Symbol, confidence, absoluteQuality, softmaxShare, margin);
    }

    private static string BuildDetails(FourierDescriptor descriptor)
    {
        var sb = new StringBuilder();
        sb.Append("<details><summary>Fourier + scanlines</summary><div class=\"mono tiny\">");
        sb.Append("H widths: ").Append(FormatDoubles(descriptor.Scanlines.HorizontalWidths)).Append("<br>");
        sb.Append("V heights: ").Append(FormatDoubles(descriptor.Scanlines.VerticalHeights)).Append("<br>");
        for (var i = 0; i < descriptor.Contours.Count; i++)
        {
            var c = descriptor.Contours[i];
            sb.Append($"C{i}: weight={c.Weight:0.###}, center=({c.CenterX:0.###},{c.CenterY:0.###}), size=({c.Width:0.###},{c.Height:0.###})<br>");
            sb.Append("magnitudes: ").Append(FormatDoubles(c.Magnitudes)).Append("<br>");
        }
        sb.Append("</div></details>");
        return sb.ToString();
    }

    private static string FormatInts(IEnumerable<int> values) => string.Join(',', values);
    private static string FormatDoubles(IEnumerable<double> values) =>
        string.Join(',', values.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)));

    private static string RelativeUrl(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/').Replace(" ", "%20", StringComparison.Ordinal);

    private sealed record Reference(string Symbol, FourierDescriptor Descriptor);
    private sealed record Distance(string Symbol, double Complex, double Magnitude, double Combined);
    private sealed record Row(
        string SourceLabel,
        string Context,
        string RawSvg,
        string RawPng,
        string NormalizedSvg,
        FourierDescriptor Descriptor,
        Distance G,
        Distance F,
        string Winner,
        double Confidence,
        double AbsoluteQuality,
        double SoftmaxShare,
        double Margin);
}
