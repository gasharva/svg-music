using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using SkiaSharp;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Diagnostic decorator around the real number recognizer. It writes exactly the vector contours
/// passed by later pipeline steps, so diagnostics never need to reopen the source SVG.
/// </summary>
public sealed class DiagnosticNumberRecognizer : ISvgNumberRecognizer
{
    private const int ImageSize = 256;
    private const float Padding = 20f;

    private readonly ISvgNumberRecognizer _inner;
    private readonly List<Entry> _entries = new();
    private string? _outputDirectory;
    private int _sequence;

    public DiagnosticNumberRecognizer(ISvgNumberRecognizer inner) => _inner = inner;

    public void BeginDocument(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        _sequence = 0;
        _entries.Clear();

        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);
        WriteIndex();
    }

    public SvgNumberRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var number = ++_sequence;
        var result = _inner.Recognize(contours);

        if (_outputDirectory is not null)
        {
            var stem = $"{number:000}";
            WritePng(contours, Path.Combine(_outputDirectory, stem + ".png"));
            WriteResult(result, contours, Path.Combine(_outputDirectory, stem + ".txt"));
            _entries.Add(new Entry(stem, result));
            WriteIndex();
        }

        return result;
    }

    private void WriteIndex()
    {
        if (_outputDirectory is null)
            return;

        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Meter recognizer inputs</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px} .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(190px,1fr));gap:16px}.card{border:1px solid #ccc;padding:10px}.card img{width:100%;max-width:256px;background:white}.mono{font-family:Consolas,monospace;font-size:12px}</style></head><body>");
        html.AppendLine("<h1>Exact inputs sent to ISvgNumberRecognizer</h1>");
        html.AppendLine("<p>Each card is one recognizer call after PrimitiveResolver. No source SVG is reread here.</p><div class=\"grid\">");

        foreach (var entry in _entries)
        {
            var result = entry.Result;
            var candidates = string.Join(", ", result.Candidates.Take(8).Select(x => $"{x.Value}:{x.Confidence:0.000}"));
            html.Append($"<div class=\"card\"><a href=\"{entry.Stem}.png\"><img src=\"{entry.Stem}.png\"></a>");
            html.Append($"<div><b>#{entry.Stem}</b> → <b>{WebUtility.HtmlEncode(result.Value?.ToString() ?? "null")}</b> ({result.Confidence:0.000})</div>");
            html.Append($"<div class=\"mono\">{WebUtility.HtmlEncode(candidates)}</div><a href=\"{entry.Stem}.txt\">details</a></div>");
        }

        html.AppendLine("</div></body></html>");
        File.WriteAllText(Path.Combine(_outputDirectory, "index.html"), html.ToString());
    }

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

    private static void WriteResult(
        SvgNumberRecognition result,
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        string outputPath)
    {
        var text = new StringBuilder();
        text.AppendLine($"contours: {contours.Count}");
        text.AppendLine($"points: {contours.Sum(x => x.Count)}");
        text.AppendLine($"value: {result.Value?.ToString(CultureInfo.InvariantCulture) ?? "null"}");
        text.AppendLine($"confidence: {result.Confidence:0.0000}");
        if (!string.IsNullOrWhiteSpace(result.Error))
            text.AppendLine($"error: {result.Error}");
        text.AppendLine("candidates:");
        foreach (var candidate in result.Candidates)
            text.AppendLine($"  {candidate.Value}: {candidate.Confidence:0.0000}");
        File.WriteAllText(outputPath, text.ToString());
    }

    private sealed record Entry(string Stem, SvgNumberRecognition Result);
}
