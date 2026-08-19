using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic overlay pass that highlights recognized rests in blue.</summary>
public sealed class RestOverlayRenderer
{
    private const float RenderScale = 2f;

    public string Render(
        PartMeasureResolution structure,
        IReadOnlyList<RestResolution> rests,
        string existingOverlayPath)
    {
        if (rests.Count == 0 || !File.Exists(existingOverlayPath))
            return existingOverlayPath;

        using var source = SKBitmap.Decode(existingOverlayPath)
            ?? throw new InvalidOperationException("Could not decode existing overlay image.");
        using var bitmap = new SKBitmap(source.Info);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(source, 0, 0);

        using var svg = SKSvg.CreateFromFile(structure.SvgPath);
        var picture = svg.Picture
            ?? throw new InvalidOperationException("Svg.Skia did not produce a renderable picture.");
        var bounds = picture.CullRect;

        canvas.Scale(RenderScale);
        canvas.Translate(-bounds.Left, -bounds.Top);

        foreach (var rest in rests)
        {
            using var fill = new SKPaint
            {
                Color = new SKColor(30, 144, 255, 85),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            using var border = new SKPaint
            {
                Color = SKColors.RoyalBlue,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.7f,
                IsAntialias = true
            };
            var rect = new SKRect(
                (float)rest.PhysicalBounds.Left,
                (float)rest.PhysicalBounds.Top,
                (float)rest.PhysicalBounds.Right,
                (float)rest.PhysicalBounds.Bottom);
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, border);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(existingOverlayPath);
        data.SaveTo(stream);
        return existingOverlayPath;
    }
}
