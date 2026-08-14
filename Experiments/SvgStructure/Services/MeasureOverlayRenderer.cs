using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed class MeasureOverlayRenderer
{
    private const float RenderScale = 2f;

    public string Render(
        string svgPath,
        IReadOnlyList<StaffSystem> systems,
        string? outputPath = null)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var picture = svg.Picture
            ?? throw new InvalidOperationException("Svg.Skia did not produce a renderable picture.");

        var bounds = picture.CullRect;
        var width = (int)Math.Ceiling(bounds.Width * RenderScale);
        var height = (int)Math.Ceiling(bounds.Height * RenderScale);

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);
        canvas.Scale(RenderScale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);

        using var paint = new SKPaint
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            IsAntialias = true
        };

        foreach (var system in systems)
        {
            for (var i = 0; i < system.BarXs.Count - 1; i++)
            {
                var rect = new SKRect(
                    (float)system.BarXs[i],
                    (float)system.Top,
                    (float)system.BarXs[i + 1],
                    (float)system.Bottom);

                canvas.DrawRect(rect, paint);
            }
        }

        outputPath ??= Path.Combine(
            Path.GetDirectoryName(svgPath) ?? ".",
            $"{Path.GetFileNameWithoutExtension(svgPath)}.measures.png");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);

        return outputPath;
    }
}
