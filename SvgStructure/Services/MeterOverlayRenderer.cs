using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic only. Dims the score and redraws recognized meters at full intensity.</summary>
public sealed class MeterOverlayRenderer
{
    private const float RenderScale = 2f;

    public string Render(
        PartMeasureResolution structure,
        IReadOnlyList<MeterResolution> meters,
        string outputPath)
    {
        using var svg = SKSvg.CreateFromFile(structure.SvgPath);
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

        using (var veil = new SKPaint { Color = new SKColor(255, 255, 255, 205) })
            canvas.DrawRect(bounds, veil);

        foreach (var meter in meters)
        {
            var r = meter.PhysicalBounds;
            var clip = new SKRect((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
            canvas.Save();
            canvas.ClipRect(clip);
            canvas.DrawPicture(picture);
            canvas.Restore();

            using var border = new SKPaint
            {
                Color = SKColors.DeepPink,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true
            };
            canvas.DrawRect(clip, border);

            DrawLabel(canvas, meter, bounds);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return outputPath;
    }

    private static void DrawLabel(SKCanvas canvas, MeterResolution meter, SKRect page)
    {
        var b = meter.PhysicalBounds;
        var digitHeight = (float)Math.Max(8, Math.Min(18, b.Height * 0.34));
        var digitWidth = digitHeight * 0.58f;
        var spacing = digitWidth * 0.24f;
        var text = $"{meter.BeatNumber}-{meter.BeatValue}";
        var totalWidth = text.Sum(ch => ch == '-' ? digitWidth * 0.55f : digitWidth + spacing);

        var x = (float)Math.Clamp(b.Left, page.Left + 2, Math.Max(page.Left + 2, page.Right - totalWidth - 2));
        var preferredTop = b.Top - digitHeight - 3;
        var y = (float)(preferredTop >= page.Top ? preferredTop : Math.Min(page.Bottom - digitHeight - 2, b.Bottom + 3));

        using var background = new SKPaint { Color = new SKColor(255, 255, 255, 235) };
        canvas.DrawRoundRect(
            new SKRect(x - 2, y - 2, x + totalWidth + 2, y + digitHeight + 2),
            2,
            2,
            background);

        using var paint = new SKPaint
        {
            Color = SKColors.DeepPink,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.2f, digitHeight * 0.10f),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        foreach (var ch in text)
        {
            if (ch == '-')
            {
                canvas.DrawLine(x, y + digitHeight * 0.5f, x + digitWidth * 0.45f, y + digitHeight * 0.5f, paint);
                x += digitWidth * 0.55f;
                continue;
            }

            DrawDigit(canvas, ch - '0', x, y, digitWidth, digitHeight, paint);
            x += digitWidth + spacing;
        }
    }

    private static void DrawDigit(SKCanvas canvas, int digit, float x, float y, float w, float h, SKPaint paint)
    {
        // Seven-segment label: avoids any font dependency on the CI runner.
        var segments = digit switch
        {
            0 => "ab cdef".Replace(" ", ""),
            1 => "bc",
            2 => "abdeg",
            3 => "abcdg",
            4 => "bcfg",
            5 => "acdfg",
            6 => "acdefg",
            7 => "abc",
            8 => "abcdefg",
            9 => "abcdfg",
            _ => string.Empty
        };

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case 'a': canvas.DrawLine(x, y, x + w, y, paint); break;
                case 'b': canvas.DrawLine(x + w, y, x + w, y + h / 2, paint); break;
                case 'c': canvas.DrawLine(x + w, y + h / 2, x + w, y + h, paint); break;
                case 'd': canvas.DrawLine(x, y + h, x + w, y + h, paint); break;
                case 'e': canvas.DrawLine(x, y + h / 2, x, y + h, paint); break;
                case 'f': canvas.DrawLine(x, y, x, y + h / 2, paint); break;
                case 'g': canvas.DrawLine(x, y + h / 2, x + w, y + h / 2, paint); break;
            }
        }
    }
}
