using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic overlay pass that highlights recognized rests in blue and labels their value.</summary>
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

            DrawValueLabel(canvas, rest, bounds);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(existingOverlayPath);
        data.SaveTo(stream);
        return existingOverlayPath;
    }

    private static void DrawValueLabel(SKCanvas canvas, RestResolution rest, SKRect page)
    {
        var text = rest.Denominator.ToString();
        var b = rest.PhysicalBounds;
        var textSize = (float)Math.Clamp(Math.Max(7.0, b.Height * 0.42), 7.0, 13.0);

        using var font = new SKFont(SKTypeface.Default, textSize);
        using var textPaint = new SKPaint
        {
            Color = SKColors.RoyalBlue,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        var textWidth = font.MeasureText(text, textPaint);
        var x = (float)Math.Clamp(
            b.CenterX - textWidth / 2.0,
            page.Left + 2,
            Math.Max(page.Left + 2, page.Right - textWidth - 2));
        var baseline = (float)(b.Top - 3);
        if (baseline - textSize < page.Top)
            baseline = (float)Math.Min(page.Bottom - 2, b.Bottom + textSize + 3);

        using var background = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(
            new SKRect(x - 2, baseline - textSize - 2, x + textWidth + 2, baseline + 2),
            2,
            2,
            background);
        canvas.DrawText(text, x, baseline, font, textPaint);
    }
}
