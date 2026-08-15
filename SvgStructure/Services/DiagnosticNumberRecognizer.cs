using System.Globalization;
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
    private string? _outputDirectory;
    private int _sequence;

    public DiagnosticNumberRecognizer(ISvgNumberRecognizer inner) => _inner = inner;

    public void BeginDocument(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        _sequence = 0;

        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);
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
        }

        return result;
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
            var scale = Math.Min(
                (ImageSize - 2 * Padding) / width,
                (ImageSize - 2 * Padding) / height);
            var drawWidth = width * scale;
            var drawHeight = height * scale;
            var offsetX = (ImageSize - drawWidth) / 2f;
            var offsetY = (ImageSize - drawHeight) / 2f;

            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var contour in contours.Where(x => x.Count >= 3))
            {
                path.MoveTo(
                    offsetX + (contour[0].X - minX) * scale,
                    offsetY + (contour[0].Y - minY) * scale);
                for (var i = 1; i < contour.Count; i++)
                {
                    path.LineTo(
                        offsetX + (contour[i].X - minX) * scale,
                        offsetY + (contour[i].Y - minY) * scale);
                }
                path.Close();
            }

            using var fill = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
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
}
