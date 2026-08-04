using SkiaSharp;
using Svg.Skia;

namespace SvgSymbolScaler;

public sealed class SvgPdfWriter(double marginMm = 5)
{
    private const float PointsPerMm = 72f / 25.4f;
    private const float A4WidthMm = 210;
    private const float A4HeightMm = 297;
    private const int ProbeLongSide = 1600;

    public void Write(IReadOnlyList<string> svgFiles, string outputPath)
    {
        if (svgFiles.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var stream = File.Create(outputPath);
        using var document = SKDocument.CreatePdf(stream);

        foreach (var file in svgFiles)
        {
            using var svg = new SKSvg();
            svg.Load(file);
            var picture = svg.Picture ?? throw new InvalidOperationException($"Could not render SVG: {file}");
            var ink = FindVisibleInk(picture);

            var portrait = CreatePage(A4WidthMm, A4HeightMm, ink);
            var landscape = CreatePage(A4HeightMm, A4WidthMm, ink);
            var page = portrait.Scale >= landscape.Scale ? portrait : landscape;

            var canvas = document.BeginPage(page.Width, page.Height);
            canvas.Clear(SKColors.White);
            canvas.ClipRect(page.Printable);
            canvas.Translate(page.OffsetX, page.OffsetY);
            canvas.Scale(page.Scale);
            canvas.Translate(-ink.Left, -ink.Top);
            canvas.DrawPicture(picture);
            document.EndPage();
        }

        document.Close();
    }

    private PageLayout CreatePage(float widthMm, float heightMm, SKRect ink)
    {
        var width = widthMm * PointsPerMm;
        var height = heightMm * PointsPerMm;
        var margin = (float)marginMm * PointsPerMm;
        var printable = new SKRect(margin, margin, width - margin, height - margin);
        var scale = Math.Min(printable.Width / ink.Width, printable.Height / ink.Height);
        var offsetX = printable.Left + (printable.Width - ink.Width * scale) / 2f;
        var offsetY = printable.Top + (printable.Height - ink.Height * scale) / 2f;
        return new PageLayout(width, height, printable, scale, offsetX, offsetY);
    }

    private static SKRect FindVisibleInk(SKPicture picture)
    {
        var source = picture.CullRect;
        if (source.Width <= 0 || source.Height <= 0)
            throw new InvalidOperationException("SVG has empty rendered bounds.");

        var factor = ProbeLongSide / Math.Max(source.Width, source.Height);
        var width = Math.Max(1, (int)Math.Ceiling(source.Width * factor));
        var height = Math.Max(1, (int)Math.Ceiling(source.Height * factor));

        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(factor);
            canvas.Translate(-source.Left, -source.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();
        }

        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.Alpha < 16 || (color.Red > 245 && color.Green > 245 && color.Blue > 245)) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        if (right < left || bottom < top) return source;

        const int safetyPixels = 2;
        left = Math.Max(0, left - safetyPixels);
        top = Math.Max(0, top - safetyPixels);
        right = Math.Min(width - 1, right + safetyPixels);
        bottom = Math.Min(height - 1, bottom + safetyPixels);

        return new SKRect(
            source.Left + left / factor,
            source.Top + top / factor,
            source.Left + (right + 1) / factor,
            source.Top + (bottom + 1) / factor);
    }

    private readonly record struct PageLayout(
        float Width,
        float Height,
        SKRect Printable,
        float Scale,
        float OffsetX,
        float OffsetY);
}
