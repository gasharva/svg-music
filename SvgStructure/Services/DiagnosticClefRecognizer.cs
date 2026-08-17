using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using SkiaSharp;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

public sealed record ClefDiagnosticContext(
    int PartNumber,
    int MeasureNumber,
    LogicalRectD LogicalBounds);

/// <summary>
/// Diagnostic decorator around IClefRecognizer. Writes exactly the glyph contours sent to the
/// current recognizer and presents them in the same compact gallery style as meter inputs.
/// Legacy IoU/skeleton experiments intentionally stay out of this report.
/// </summary>
public sealed class DiagnosticClefRecognizer : IClefRecognizer
{
    private const int ImageSize = 256;
    private const float Padding = 20f;

    private readonly IClefRecognizer _inner;
    private readonly List<Entry> _entries = new();
    private string? _outputDirectory;
    private ClefDiagnosticContext? _nextContext;
    private int _sequence;

    public DiagnosticClefRecognizer(IClefRecognizer inner, LegacyIoUClefAnalyzer? legacyIoU = null)
    {
        _inner = inner;
    }

    public void BeginDocument(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        _sequence = 0;
        _nextContext = null;
        _entries.Clear();

        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);
        WriteIndex();
    }

    public void SetNextContext(ClefDiagnosticContext context) => _nextContext = context;

    public ClefSymbolRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var context = _nextContext;
        _nextContext = null;
        var result = _inner.Recognize(contours);

        if (_outputDirectory is not null)
        {
            var stem = (++_sequence).ToString("000", CultureInfo.InvariantCulture);
            WritePng(contours, Path.Combine(_outputDirectory, stem + ".png"));
            WriteSvg(contours, Path.Combine(_outputDirectory, stem + ".svg"));
            WriteResult(result, contours, context, Path.Combine(_outputDirectory, stem + ".txt"));
            _entries.Add(new Entry(stem, result, context));
            WriteIndex();
        }

        return result;
    }

    private void WriteIndex()
    {
        if (_outputDirectory is null)
            return;

        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Clef recognizer inputs</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:16px}.card{border:1px solid #ccc;padding:10px}.card img{width:100%;max-width:256px;background:white}.mono{font-family:Consolas,monospace;font-size:12px}.muted{color:#666}</style></head><body>");
        html.AppendLine("<h1>Exact inputs sent to IClefRecognizer</h1>");
        html.AppendLine("<p>Each card is one post-sanity-filter glyph candidate. This report shows only the current recognizer verdict; old Legacy IoU/skeleton diagnostics are intentionally omitted.</p><div class=\"grid\">");

        foreach (var entry in _entries)
        {
            var result = entry.Result;
            var answer = result.Symbol?.ToString() ?? "null";
            var candidates = string.Join(", ", result.Candidates.Take(6)
                .Select(x => $"{x.Symbol}:{x.Confidence:0.000} d={x.Distance:0.###}"));
            var pm = entry.Context is null
                ? "n/a"
                : $"P{entry.Context.PartNumber}-M{entry.Context.MeasureNumber}";

            html.Append($"<div class=\"card\"><a href=\"{entry.Stem}.png\"><img src=\"{entry.Stem}.png\"></a>");
            html.Append($"<div><b>#{entry.Stem}</b> → <b>{WebUtility.HtmlEncode(answer)}</b> ({result.Confidence:0.000})</div>");
            html.Append($"<div class=\"muted\">{WebUtility.HtmlEncode(pm)}</div>");
            html.Append($"<div class=\"mono\">{WebUtility.HtmlEncode(candidates)}</div>");
            if (!string.IsNullOrWhiteSpace(result.Error))
                html.Append($"<div class=\"mono muted\">{WebUtility.HtmlEncode(result.Error)}</div>");
            html.Append($"<a href=\"{entry.Stem}.svg\">svg</a> · <a href=\"{entry.Stem}.txt\">details</a></div>");
        }

        html.AppendLine("</div></body></html>");
        File.WriteAllText(Path.Combine(_outputDirectory, "index.html"), html.ToString());

        var markdown = new StringBuilder();
        markdown.AppendLine("# Clef recognizer inputs");
        markdown.AppendLine();
        markdown.AppendLine("Exact post-sanity-filter glyph candidates sent to the current `IClefRecognizer`.");
        markdown.AppendLine();
        foreach (var entry in _entries)
        {
            var answer = entry.Result.Symbol?.ToString() ?? "null";
            markdown.AppendLine($"- [{entry.Stem}]({entry.Stem}.txt) → **{answer}** ({entry.Result.Confidence:0.000}) · [svg]({entry.Stem}.svg) · [png]({entry.Stem}.png)");
        }
        File.WriteAllText(Path.Combine(_outputDirectory, "README.md"), markdown.ToString());
    }

    private static void WriteResult(
        ClefSymbolRecognition result,
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        ClefDiagnosticContext? context,
        string outputPath)
    {
        var text = new StringBuilder();
        text.AppendLine(context is null
            ? "block: n/a"
            : $"block: P{context.PartNumber}-M{context.MeasureNumber}");
        if (context is not null)
            text.AppendLine($"logical bbox: {Format(context.LogicalBounds)}");
        text.AppendLine($"contours: {contours.Count}");
        text.AppendLine($"points: {contours.Sum(x => x.Count)}");
        text.AppendLine($"value: {result.Symbol?.ToString() ?? "null"}");
        text.AppendLine($"confidence: {result.Confidence:0.0000}");
        if (!string.IsNullOrWhiteSpace(result.Error))
            text.AppendLine($"error: {result.Error}");
        text.AppendLine("candidates:");
        foreach (var candidate in result.Candidates)
            text.AppendLine($"  {candidate.Symbol}: confidence={candidate.Confidence:0.0000}, distance={candidate.Distance:0.####}");
        File.WriteAllText(outputPath, text.ToString());
    }

    private static string Format(LogicalRectD b) =>
        $"X {Fmt(b.Left)}..{Fmt(b.Right)}, Y {b.Top:0.##}..{b.Bottom:0.##}";

    private static string Fmt(double? value) =>
        value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?";

    private static void WritePng(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        string outputPath)
    {
        using var bitmap = new SKBitmap(ImageSize, ImageSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var points = contours.SelectMany(x => x).ToArray();
        if (points.Length > 0)
        {
            var minX = points.Min(x => x.X);
            var minY = points.Min(x => x.Y);
            var maxX = points.Max(x => x.X);
            var maxY = points.Max(x => x.Y);
            var width = Math.Max(1e-6f, maxX - minX);
            var height = Math.Max(1e-6f, maxY - minY);
            var scale = Math.Min((ImageSize - 2 * Padding) / width, (ImageSize - 2 * Padding) / height);
            var offsetX = (ImageSize - width * scale) / 2f;
            var offsetY = (ImageSize - height * scale) / 2f;

            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var contour in contours.Where(x => x.Count >= 3))
            {
                path.MoveTo(offsetX + (contour[0].X - minX) * scale, offsetY + (contour[0].Y - minY) * scale);
                for (var i = 1; i < contour.Count; i++)
                    path.LineTo(offsetX + (contour[i].X - minX) * scale, offsetY + (contour[i].Y - minY) * scale);
                path.Close();
            }

            using var fill = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, fill);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
    }

    private static void WriteSvg(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        string outputPath)
    {
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
        var padding = Math.Max(width, height) * 0.05f;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
            .Append(Fmt(minX - padding)).Append(' ')
            .Append(Fmt(minY - padding)).Append(' ')
            .Append(Fmt(width + padding * 2)).Append(' ')
            .Append(Fmt(height + padding * 2)).AppendLine("\">");
        sb.AppendLine("  <path fill=\"black\" fill-rule=\"evenodd\" d=\"");
        foreach (var contour in contours.Where(x => x.Count >= 3))
        {
            sb.Append("    M ").Append(Fmt(contour[0].X)).Append(' ').Append(Fmt(contour[0].Y));
            for (var i = 1; i < contour.Count; i++)
                sb.Append(" L ").Append(Fmt(contour[i].X)).Append(' ').Append(Fmt(contour[i].Y));
            sb.AppendLine(" Z");
        }
        sb.AppendLine("  \"/>");
        sb.AppendLine("</svg>");
        File.WriteAllText(outputPath, sb.ToString());
    }

    private static string Fmt(float value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private sealed record Entry(
        string Stem,
        ClefSymbolRecognition Result,
        ClefDiagnosticContext? Context);
}
