using System.Globalization;
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
/// Diagnostic wrapper around IClefRecognizer. Saves exactly the vector contours that survive
/// ClefResolver's sanity filters and are sent to the recognizer.
/// </summary>
public sealed class DiagnosticClefRecognizer : IClefRecognizer
{
    private readonly IClefRecognizer _inner;
    private string? _outputDirectory;
    private ClefDiagnosticContext? _nextContext;
    private int _counter;

    public DiagnosticClefRecognizer(IClefRecognizer inner) => _inner = inner;

    public void BeginDocument(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        _counter = 0;
        _nextContext = null;

        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        File.WriteAllText(
            Path.Combine(outputDirectory, "README.md"),
            "# Clef recognizer inputs\n\n" +
            "These are the exact post-sanity-filter vector candidates sent to `IClefRecognizer`.\n\n" +
            "| Candidate | P+M | Logical bbox | Recognizer | Shape |\n" +
            "|---|---|---|---|---|\n");
    }

    public void SetNextContext(ClefDiagnosticContext context) => _nextContext = context;

    public ClefSymbolRecognition Recognize(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var result = _inner.Recognize(contours);
        Save(contours, result, _nextContext);
        _nextContext = null;
        return result;
    }

    private void Save(
        IReadOnlyList<IReadOnlyList<Vector2>> contours,
        ClefSymbolRecognition result,
        ClefDiagnosticContext? context)
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory))
            return;

        var id = (++_counter).ToString("000", CultureInfo.InvariantCulture);
        var svgName = id + ".svg";
        var pngName = id + ".png";
        var txtName = id + ".txt";

        WriteSvg(Path.Combine(_outputDirectory, svgName), contours);
        WritePng(Path.Combine(_outputDirectory, pngName), contours);

        var logical = context is null
            ? "n/a"
            : Format(context.LogicalBounds);
        var pm = context is null
            ? "n/a"
            : $"P{context.PartNumber}-M{context.MeasureNumber}";
        var answer = result.Symbol is null
            ? $"none ({result.Error ?? "no result"})"
            : $"{result.Symbol} {result.Confidence:P1}";
        var candidates = string.Join(
            Environment.NewLine,
            result.Candidates.Select(x => $"{x.Symbol}: confidence={x.Confidence:P2}, distance={x.Distance:0.###}"));

        File.WriteAllText(
            Path.Combine(_outputDirectory, txtName),
            $"block: {pm}{Environment.NewLine}" +
            $"logical bbox: {logical}{Environment.NewLine}" +
            $"contours: {contours.Count}{Environment.NewLine}" +
            $"points: {contours.Sum(x => x.Count)}{Environment.NewLine}" +
            $"result: {answer}{Environment.NewLine}{Environment.NewLine}" +
            "candidates:" + Environment.NewLine + candidates + Environment.NewLine);

        File.AppendAllText(
            Path.Combine(_outputDirectory, "README.md"),
            $"| [{id}]({txtName}) | {pm} | `{logical}` | {answer} | ![{id}]({pngName}) |{Environment.NewLine}");
    }

    private static string Format(LogicalRectD b) =>
        $"X {Fmt(b.Left)}..{Fmt(b.Right)}, Y {b.Top:0.##}..{b.Bottom:0.##}";

    private static string Fmt(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?";

    private static void WritePng(string path, IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        const int size = 220;
        const float padding = 18f;
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var bounds = Bounds(contours);
        if (bounds.Width > 1e-6 && bounds.Height > 1e-6)
        {
            var scale = Math.Min((size - 2 * padding) / bounds.Width, (size - 2 * padding) / bounds.Height);
            var dx = padding + ((size - 2 * padding) - bounds.Width * scale) / 2f;
            var dy = padding + ((size - 2 * padding) - bounds.Height * scale) / 2f;

            canvas.Translate(dx, dy);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);

            using var pathShape = BuildPath(contours);
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawPath(pathShape, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static void WriteSvg(string path, IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var bounds = Bounds(contours);
        var pad = Math.Max(1d, Math.Max(bounds.Width, bounds.Height) * 0.06);
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{bounds.Left - pad:0.####} {bounds.Top - pad:0.####} {bounds.Width + 2 * pad:0.####} {bounds.Height + 2 * pad:0.####}\">");
        sb.Append("<path fill=\"black\" fill-rule=\"evenodd\" d=\"");
        foreach (var contour in contours.Where(x => x.Count >= 2))
        {
            sb.Append($"M {contour[0].X.ToString("0.####", CultureInfo.InvariantCulture)} {contour[0].Y.ToString("0.####", CultureInfo.InvariantCulture)} ");
            foreach (var p in contour.Skip(1))
                sb.Append($"L {p.X.ToString("0.####", CultureInfo.InvariantCulture)} {p.Y.ToString("0.####", CultureInfo.InvariantCulture)} ");
            sb.Append("Z ");
        }
        sb.AppendLine("\"/></svg>");
        File.WriteAllText(path, sb.ToString());
    }

    private static SKPath BuildPath(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var contour in contours.Where(x => x.Count >= 2))
        {
            path.MoveTo(contour[0].X, contour[0].Y);
            foreach (var p in contour.Skip(1))
                path.LineTo(p.X, p.Y);
            path.Close();
        }
        return path;
    }

    private static SKRect Bounds(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var points = contours.SelectMany(x => x).ToArray();
        if (points.Length == 0)
            return new SKRect(0, 0, 1, 1);

        return new SKRect(
            points.Min(x => x.X),
            points.Min(x => x.Y),
            points.Max(x => x.X),
            points.Max(x => x.Y));
    }
}
