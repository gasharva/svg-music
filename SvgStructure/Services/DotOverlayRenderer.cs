using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic overlay: recognized dots and their attached note/rest targets are red.</summary>
public sealed class DotOverlayRenderer
{
    private const float RenderScale = 2f;

    public string Render(
        PartMeasureResolution structure,
        IReadOnlyList<DotResolution> dots,
        string existingOverlayPath)
    {
        if (dots.Count == 0 || !File.Exists(existingOverlayPath))
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

        foreach (var dot in dots)
        {
            using var dotFill = new SKPaint
            {
                Color = new SKColor(255, 0, 0, 145),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            using var redBorder = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.8f,
                IsAntialias = true
            };

            canvas.DrawOval(ToRect(dot.PhysicalBounds), dotFill);
            canvas.DrawOval(ToRect(dot.PhysicalBounds), redBorder);

            if (dot.Note is not null)
                canvas.DrawOval(ToRect(dot.Note.PhysicalBounds), redBorder);
            else if (dot.Rest is not null)
                canvas.DrawRect(ToRect(dot.Rest.PhysicalBounds), redBorder);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(existingOverlayPath);
        data.SaveTo(stream);
        return existingOverlayPath;
    }

    private static SKRect ToRect(RectD r) =>
        new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
}
