using System.Numerics;
using SkiaSharp;

namespace SvgSymbols.Services;

public sealed record RasterClefScore(
    ClefSymbol Symbol,
    double Distance,
    double Similarity);

public sealed record RasterClefAnalysis(
    ClefSymbol? Symbol,
    double Confidence,
    IReadOnlyList<RasterClefScore> Candidates,
    int Size);

/// <summary>
/// Deliberately simple raster baseline for clef recognition.
/// Both references and candidates are normalized to the same small grayscale bitmap and compared
/// pixel-by-pixel. Bravura references are rasterized once in the constructor and kept in memory.
/// </summary>
public sealed class RasterClefAnalyzer
{
    private readonly int _size;
    private readonly IReadOnlyDictionary<ClefSymbol, float[]> _references;

    public RasterClefAnalyzer(string referenceGlyphDirectory, int size = 48)
    {
        _size = Math.Clamp(size, 24, 128);
        _references = new Dictionary<ClefSymbol, float[]>
        {
            [ClefSymbol.G] = RasterizeSvg(Path.Combine(referenceGlyphDirectory, "gClef.svg")),
            [ClefSymbol.F] = RasterizeSvg(Path.Combine(referenceGlyphDirectory, "fClef.svg"))
        };
    }

    public RasterClefAnalysis Analyze(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        if (contours.Count == 0)
            return new RasterClefAnalysis(null, 0, Array.Empty<RasterClefScore>(), _size);

        var candidate = RasterizeContours(contours);
        var ranked = _references
            .Select(x =>
            {
                var distance = MeanAbsoluteDistance(candidate, x.Value);
                return new RasterClefScore(x.Key, distance, 1d - distance);
            })
            .OrderBy(x => x.Distance)
            .ToArray();

        if (ranked.Length == 0)
            return new RasterClefAnalysis(null, 0, Array.Empty<RasterClefScore>(), _size);

        var best = ranked[0];
        var second = ranked.Length > 1 ? ranked[1] : null;
        var margin = second is null ? 0d : second.Distance - best.Distance;
        var confidence = Math.Clamp(best.Similarity * Math.Clamp(margin * 4d, 0d, 1d), 0d, 1d);
        return new RasterClefAnalysis(best.Symbol, confidence, ranked, _size);
    }

    private float[] RasterizeSvg(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Bravura clef reference not found.", path);

        using var svg = new Svg.Skia.SKSvg();
        svg.Load(path);
        var picture = svg.Picture ?? throw new InvalidOperationException($"Could not load SVG '{path}'.");
        return Render(picture);
    }

    private float[] RasterizeContours(IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var contour in contours.Where(x => x.Count >= 3))
        {
            path.MoveTo(contour[0].X, contour[0].Y);
            foreach (var point in contour.Skip(1))
                path.LineTo(point.X, point.Y);
            path.Close();
        }

        var bounds = path.Bounds;
        if (bounds.Width <= 1e-6 || bounds.Height <= 1e-6)
            return new float[_size * _size];

        using var pictureRecorder = new SKPictureRecorder();
        var canvas = pictureRecorder.BeginRecording(bounds);
        using var paint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawPath(path, paint);
        using var picture = pictureRecorder.EndRecording();
        return Render(picture);
    }

    private float[] Render(SKPicture picture)
    {
        using var bitmap = new SKBitmap(_size, _size, SKColorType.Gray8, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var bounds = picture.CullRect;
        if (bounds.Width > 1e-6 && bounds.Height > 1e-6)
        {
            const float paddingFraction = 0.08f;
            var available = _size * (1f - 2f * paddingFraction);
            var scale = Math.Min(available / bounds.Width, available / bounds.Height);
            var dx = (_size - bounds.Width * scale) / 2f;
            var dy = (_size - bounds.Height * scale) / 2f;
            canvas.Translate(dx, dy);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }

        var result = new float[_size * _size];
        for (var y = 0; y < _size; y++)
        for (var x = 0; x < _size; x++)
            result[y * _size + x] = 1f - bitmap.GetPixel(x, y).Red / 255f;
        return result;
    }

    private static double MeanAbsoluteDistance(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        var count = Math.Min(a.Count, b.Count);
        if (count == 0)
            return 1d;

        double sum = 0;
        for (var i = 0; i < count; i++)
            sum += Math.Abs(a[i] - b[i]);
        return sum / count;
    }
}
