using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic overlay: highlights resolved arpeggiato marks in red on the semantic-symbol image.</summary>
public sealed class ArpeggiatoOverlayRenderer
{
    public string Render(
        PartMeasureResolution structure,
        IReadOnlyList<ArpeggiatoResolution> arpeggiati,
        string imagePath)
    {
        if (arpeggiati.Count == 0 || !File.Exists(imagePath))
            return imagePath;

        using var svg = SKSvg.CreateFromFile(structure.SvgPath);
        var picture = svg.Picture
            ?? throw new InvalidOperationException("Svg.Skia did not produce a renderable picture.");
        var page = picture.CullRect;

        using var source = SKBitmap.Decode(imagePath)
            ?? throw new InvalidOperationException($"Could not decode diagnostic image '{imagePath}'.");
        using var bitmap = new SKBitmap(source.Info);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(source, 0, 0);

        var scaleX = source.Width / Math.Max(1e-9f, page.Width);
        var scaleY = source.Height / Math.Max(1e-9f, page.Height);

        using var fill = new SKPaint
        {
            Color = new SKColor(255, 0, 0, 75),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var border = new SKPaint
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = true
        };

        foreach (var arpeggiato in arpeggiati)
        {
            var b = arpeggiato.PhysicalBounds;
            var rect = new SKRect(
                (float)((b.Left - page.Left) * scaleX),
                (float)((b.Top - page.Top) * scaleY),
                (float)((b.Right - page.Left) * scaleX),
                (float)((b.Bottom - page.Top) * scaleY));
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, border);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(imagePath);
        data.SaveTo(stream);
        return imagePath;
    }
}
