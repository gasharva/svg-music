using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic only. Renders step-1 logical blocks; it does not participate in recognition.</summary>
public sealed class PartMeasureOverlayRenderer
{
    private const float RenderScale = 2f;

    public string Render(PartMeasureResolution resolution, string outputPath)
    {
        using var svg = SKSvg.CreateFromFile(resolution.SvgPath);
        var picture = svg.Picture
            ?? throw new InvalidOperationException("Svg.Skia did not produce a renderable picture.");

        var bounds = picture.CullRect;
        using var bitmap = new SKBitmap(
            (int)Math.Ceiling(bounds.Width * RenderScale),
            (int)Math.Ceiling(bounds.Height * RenderScale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
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

        foreach (var block in resolution.Map.Blocks)
        {
            var b = block.PhysicalBounds;
            canvas.DrawRect(new SKRect((float)b.Left, (float)b.Top, (float)b.Right, (float)b.Bottom), paint);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return outputPath;
    }
}
