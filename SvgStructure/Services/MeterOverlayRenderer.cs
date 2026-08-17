using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic only. Dims the score and redraws recognized meters and clefs at full intensity.</summary>
public sealed class MeterOverlayRenderer
{
    private const float RenderScale = 2f;

    public string Render(
        PartMeasureResolution structure,
        IReadOnlyList<MeterResolution> meters,
        IReadOnlyList<ClefResolution> clefs,
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
            RedrawRegion(canvas, picture, meter.PhysicalBounds);
            using var border = Border(SKColors.DeepPink);
            canvas.DrawRect(ToRect(meter.PhysicalBounds), border);
            DrawMeterLabel(canvas, meter, bounds);
        }

        foreach (var clef in clefs)
        {
            RedrawRegion(canvas, picture, clef.PhysicalBounds);
            using var border = Border(SKColors.DodgerBlue);
            canvas.DrawRect(ToRect(clef.PhysicalBounds), border);
            DrawClefLabel(canvas, clef, bounds);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return outputPath;
    }

    private static void RedrawRegion(SKCanvas canvas, SKPicture picture, RectD region)
    {
        var clip = ToRect(region);
        canvas.Save();
        canvas.ClipRect(clip);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static SKPaint Border(SKColor color) => new()
    {
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        IsAntialias = true
    };

    private static SKRect ToRect(RectD r) =>
        new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);

    private static void DrawMeterLabel(SKCanvas canvas, MeterResolution meter, SKRect page)
    {
        var b = meter.PhysicalBounds;
        var height = (float)Math.Max(8, Math.Min(18, b.Height * 0.34));
        DrawVectorLabel(
            canvas,
            $"{meter.BeatNumber}-{meter.BeatValue}",
            b.Left,
            b.Top,
            b.Bottom,
            height,
            SKColors.DeepPink,
            page);
    }

    private static void DrawClefLabel(SKCanvas canvas, ClefResolution clef, SKRect page)
    {
        var b = clef.PhysicalBounds;
        var height = (float)Math.Max(8, Math.Min(14, b.Height * 0.20));
        DrawVectorLabel(
            canvas,
            clef.Kind.ToString(),
            b.Left,
            b.Top,
            b.Bottom,
            height,
            SKColors.DodgerBlue,
            page);
    }

    private static void DrawVectorLabel(
        SKCanvas canvas,
        string text,
        double left,
        double top,
        double bottom,
        float height,
        SKColor color,
        SKRect page)
    {
        var charWidth = height * 0.58f;
        var spacing = charWidth * 0.24f;
        var totalWidth = text.Sum(ch => CharAdvance(ch, charWidth, spacing));
        var x = (float)Math.Clamp(left, page.Left + 2, Math.Max(page.Left + 2, page.Right - totalWidth - 2));
        var preferredTop = top - height - 3;
        var y = (float)(preferredTop >= page.Top ? preferredTop : Math.Min(page.Bottom - height - 2, bottom + 3));

        using var background = new SKPaint { Color = new SKColor(255, 255, 255, 238) };
        canvas.DrawRoundRect(new SKRect(x - 2, y - 2, x + totalWidth + 2, y + height + 2), 2, 2, background);

        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.0f, height * 0.09f),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        foreach (var ch in text)
        {
            DrawChar(canvas, char.ToUpperInvariant(ch), x, y, charWidth, height, paint);
            x += CharAdvance(ch, charWidth, spacing);
        }
    }

    private static float CharAdvance(char ch, float width, float spacing) =>
        ch == ' ' ? width * 0.55f : width + spacing;

    private static void DrawChar(SKCanvas canvas, char ch, float x, float y, float w, float h, SKPaint paint)
    {
        if (char.IsDigit(ch))
        {
            DrawDigit(canvas, ch - '0', x, y, w, h, paint);
            return;
        }

        switch (ch)
        {
            case '-': canvas.DrawLine(x, y + h * .5f, x + w * .75f, y + h * .5f, paint); break;
            case '.': canvas.DrawPoint(x + w * .35f, y + h, paint); break;
            case '?':
                canvas.DrawLine(x, y, x + w, y, paint);
                canvas.DrawLine(x + w, y, x + w, y + h * .45f, paint);
                canvas.DrawLine(x + w, y + h * .45f, x + w * .45f, y + h * .65f, paint);
                canvas.DrawPoint(x + w * .45f, y + h, paint);
                break;
            case 'G':
                canvas.DrawOval(new SKRect(x, y, x + w, y + h), paint);
                canvas.DrawLine(x + w * .52f, y + h * .55f, x + w, y + h * .55f, paint);
                canvas.DrawLine(x + w, y + h * .55f, x + w, y + h * .82f, paint);
                break;
            case 'F':
                canvas.DrawLine(x, y, x, y + h, paint);
                canvas.DrawLine(x, y, x + w, y, paint);
                canvas.DrawLine(x, y + h * .48f, x + w * .75f, y + h * .48f, paint);
                break;
            case 'C':
                canvas.DrawArc(new SKRect(x, y, x + w, y + h), 45, 270, false, paint);
                break;
            case 'X':
                canvas.DrawLine(x, y, x + w, y + h, paint);
                canvas.DrawLine(x + w, y, x, y + h, paint);
                break;
            case 'Y':
                canvas.DrawLine(x, y, x + w * .5f, y + h * .5f, paint);
                canvas.DrawLine(x + w, y, x + w * .5f, y + h * .5f, paint);
                canvas.DrawLine(x + w * .5f, y + h * .5f, x + w * .5f, y + h, paint);
                break;
        }
    }

    private static void DrawDigit(SKCanvas canvas, int digit, float x, float y, float w, float h, SKPaint paint)
    {
        var segments = digit switch
        {
            0 => "abcdef",
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
