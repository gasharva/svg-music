using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Adds note-flag diagnostics to the already rendered common meters.png overlay.</summary>
public sealed class NoteFlagOverlayRenderer
{
    private const float RenderScale = 2f;

    public void Render(
        PartMeasureResolution structure,
        IReadOnlyList<NoteFlagResolution> flags,
        string overlayPath)
    {
        if (flags.Count == 0 || !File.Exists(overlayPath))
            return;

        using var svg = SKSvg.CreateFromFile(structure.SvgPath);
        var picture = svg.Picture
            ?? throw new InvalidOperationException("Svg.Skia did not produce a renderable picture.");
        var page = picture.CullRect;

        using var bitmap = SKBitmap.Decode(overlayPath)
            ?? throw new InvalidOperationException($"Could not open overlay '{overlayPath}'.");
        using var canvas = new SKCanvas(bitmap);

        using var fill = new SKPaint
        {
            Color = new SKColor(255, 0, 0, 85),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var border = new SKPaint
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.2f,
            IsAntialias = true
        };

        foreach (var flag in flags)
        {
            var b = flag.PhysicalBounds;
            var rect = new SKRect(
                (float)((b.Left - page.Left) * RenderScale),
                (float)((b.Top - page.Top) * RenderScale),
                (float)((b.Right - page.Left) * RenderScale),
                (float)((b.Bottom - page.Top) * RenderScale));
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, border);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(overlayPath);
        data.SaveTo(stream);
    }
}
