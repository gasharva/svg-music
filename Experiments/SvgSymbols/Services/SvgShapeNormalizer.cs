using Shim = ShimSkiaSharp;
using Skia = SkiaSharp;
using Svg.Skia;

namespace SvgSymbols.Services;

/// <summary>
/// Uses Svg.Skia only to resolve the SVG scene graph (<use>, nested pictures and transforms),
/// then converts the retained ShimSkiaSharp paths to real SkiaSharp paths so PathOps can
/// merge all overlapping filled geometry into a simplified silhouette.
/// </summary>
public sealed class SvgShapeNormalizer
{
    private const int EllipseSteps = 64;

    public NormalizedShapeResult NormalizeToFile(string sourcePath, string outputPath)
    {
        using var normalized = Normalize(sourcePath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        WriteSvg(normalized, outputPath);

        return new NormalizedShapeResult(
            outputPath,
            normalized.Bounds.Width,
            normalized.Bounds.Height);
    }

    public Skia.SKPath Normalize(string sourcePath)
    {
        using var svg = SKSvg.CreateFromFile(sourcePath);
        var picture = svg.Model
            ?? throw new InvalidOperationException(
                $"Svg.Skia did not produce a retained scene model for '{sourcePath}'.");

        Skia.SKPath? merged = null;

        try
        {
            ReadPicture(picture, Shim.SKMatrix.Identity, path =>
            {
                if (path.IsEmpty)
                    return;

                if (merged is null)
                {
                    merged = new Skia.SKPath();
                    merged.AddPath(path);
                    return;
                }

                var union = merged.Op(path, Skia.SKPathOp.Union);
                if (union is null)
                    throw new InvalidOperationException("SkiaSharp PathOps union failed while normalizing SVG.");

                merged.Dispose();
                merged = union;
            });

            if (merged is null || merged.IsEmpty)
                throw new InvalidOperationException($"No drawable paths found in '{sourcePath}'.");

            // Some Wikimedia SVGs keep a whole strip/sprite of glyph geometry outside the
            // visible SVG viewport. Svg.Skia correctly clips it while rendering, but our
            // previous manual DrawPath walk ignored that clipping and therefore exposed the
            // neighbouring digits in the normalized output. Clip the merged geometry to the
            // root picture's cull rect before Simplify(), so normalization describes only what
            // is actually visible in this SVG.
            var cull = picture.CullRect;
            if (cull.Width > 0 && cull.Height > 0)
            {
                using var viewport = new Skia.SKPath();
                viewport.AddRect(new Skia.SKRect(cull.Left, cull.Top, cull.Right, cull.Bottom));

                var clipped = merged.Op(viewport, Skia.SKPathOp.Intersect);
                if (clipped is not null)
                {
                    merged.Dispose();
                    merged = clipped;
                }
            }

            var simplified = merged.Simplify();
            if (simplified is null)
                return merged;

            merged.Dispose();
            merged = null;
            return simplified;
        }
        catch
        {
            merged?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Normalizes already extracted raw vector contours. This is the entry point intended for
    /// the eventual score classifier: callers do not need to reconstruct an SVG document first.
    /// Contours are expected in one common coordinate system.
    /// </summary>
    public Skia.SKPath NormalizeContours(IReadOnlyList<IReadOnlyList<System.Numerics.Vector2>> contours)
    {
        Skia.SKPath? merged = null;
        try
        {
            foreach (var contour in contours)
            {
                if (contour.Count < 3)
                    continue;

                using var path = new Skia.SKPath();
                path.MoveTo(contour[0].X, contour[0].Y);
                for (var i = 1; i < contour.Count; i++)
                    path.LineTo(contour[i].X, contour[i].Y);
                path.Close();

                if (merged is null)
                {
                    merged = new Skia.SKPath();
                    merged.AddPath(path);
                    continue;
                }

                var union = merged.Op(path, Skia.SKPathOp.Union);
                if (union is null)
                    continue;

                merged.Dispose();
                merged = union;
            }

            if (merged is null || merged.IsEmpty)
                throw new InvalidOperationException("No usable raw contours were supplied.");

            var simplified = merged.Simplify();
            if (simplified is null)
                return merged;

            merged.Dispose();
            merged = null;
            return simplified;
        }
        catch
        {
            merged?.Dispose();
            throw;
        }
    }

    public void WriteNormalizedPath(Skia.SKPath path, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        WriteSvg(path, outputPath);
    }

    private static void ReadPicture(
        Shim.SKPicture picture,
        Shim.SKMatrix parentMatrix,
        Action<Skia.SKPath> onPath)
    {
        if (picture.Commands is null)
            return;

        var matrix = parentMatrix;
        var stack = new Stack<Shim.SKMatrix>();

        foreach (var command in picture.Commands)
        {
            switch (command)
            {
                case Shim.SaveCanvasCommand:
                case Shim.SaveLayerCanvasCommand:
                    stack.Push(matrix);
                    break;

                case Shim.RestoreCanvasCommand:
                    if (stack.Count > 0)
                        matrix = stack.Pop();
                    break;

                case Shim.SetMatrixCanvasCommand setMatrix:
                    matrix = parentMatrix.PreConcat(setMatrix.TotalMatrix);
                    break;

                case Shim.DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                {
                    using var path = ConvertPath(drawPath.Path, matrix);
                    onPath(path);
                    break;
                }

                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    ReadPicture(drawPicture.Picture, matrix, onPath);
                    break;
            }
        }
    }

    private static Skia.SKPath ConvertPath(Shim.SKPath source, Shim.SKMatrix matrix)
    {
        var result = new Skia.SKPath
        {
            FillType = source.FillType == Shim.SKPathFillType.EvenOdd
                ? Skia.SKPathFillType.EvenOdd
                : Skia.SKPathFillType.Winding
        };

        Skia.SKPoint current = default;
        Skia.SKPoint start = default;
        var hasCurrent = false;

        foreach (var command in source)
        {
            switch (command)
            {
                case Shim.MoveToPathCommand move:
                {
                    var p = Map(matrix, move.X, move.Y);
                    result.MoveTo(p);
                    current = start = p;
                    hasCurrent = true;
                    break;
                }

                case Shim.LineToPathCommand line when hasCurrent:
                {
                    var p = Map(matrix, line.X, line.Y);
                    result.LineTo(p);
                    current = p;
                    break;
                }

                case Shim.QuadToPathCommand quad when hasCurrent:
                {
                    var c = Map(matrix, quad.X0, quad.Y0);
                    var p = Map(matrix, quad.X1, quad.Y1);
                    result.QuadTo(c, p);
                    current = p;
                    break;
                }

                case Shim.CubicToPathCommand cubic when hasCurrent:
                {
                    var c1 = Map(matrix, cubic.X0, cubic.Y0);
                    var c2 = Map(matrix, cubic.X1, cubic.Y1);
                    var p = Map(matrix, cubic.X2, cubic.Y2);
                    result.CubicTo(c1, c2, p);
                    current = p;
                    break;
                }

                case Shim.ArcToPathCommand arc when hasCurrent:
                {
                    var p = Map(matrix, arc.X, arc.Y);
                    result.LineTo(p);
                    current = p;
                    break;
                }

                case Shim.ClosePathCommand when hasCurrent:
                    result.Close();
                    current = start;
                    break;

                case Shim.AddPolyPathCommand poly:
                    AddPoly(result, poly, matrix);
                    hasCurrent = false;
                    break;

                case Shim.AddRectPathCommand rect:
                    AddRect(result, rect.Rect, matrix);
                    hasCurrent = false;
                    break;

                case Shim.AddRoundRectPathCommand roundRect:
                    AddRect(result, roundRect.Rect, matrix);
                    hasCurrent = false;
                    break;

                case Shim.AddCirclePathCommand circle:
                    AddEllipse(result, circle.X, circle.Y, circle.Radius, circle.Radius, matrix);
                    hasCurrent = false;
                    break;

                case Shim.AddOvalPathCommand oval:
                    AddEllipse(
                        result,
                        (oval.Rect.Left + oval.Rect.Right) / 2f,
                        (oval.Rect.Top + oval.Rect.Bottom) / 2f,
                        oval.Rect.Width / 2f,
                        oval.Rect.Height / 2f,
                        matrix);
                    hasCurrent = false;
                    break;
            }
        }

        return result;
    }

    private static void AddPoly(Skia.SKPath path, Shim.AddPolyPathCommand poly, Shim.SKMatrix matrix)
    {
        if (poly.Count <= 0)
            return;

        var first = Map(matrix, poly[0].X, poly[0].Y);
        path.MoveTo(first);

        for (var i = 1; i < poly.Count; i++)
        {
            var p = Map(matrix, poly[i].X, poly[i].Y);
            path.LineTo(p);
        }

        if (poly.Close)
            path.Close();
    }

    private static void AddRect(Skia.SKPath path, Shim.SKRect rect, Shim.SKMatrix matrix)
    {
        var p0 = Map(matrix, rect.Left, rect.Top);
        var p1 = Map(matrix, rect.Right, rect.Top);
        var p2 = Map(matrix, rect.Right, rect.Bottom);
        var p3 = Map(matrix, rect.Left, rect.Bottom);

        path.MoveTo(p0);
        path.LineTo(p1);
        path.LineTo(p2);
        path.LineTo(p3);
        path.Close();
    }

    private static void AddEllipse(
        Skia.SKPath path,
        float cx,
        float cy,
        float rx,
        float ry,
        Shim.SKMatrix matrix)
    {
        for (var i = 0; i <= EllipseSteps; i++)
        {
            var angle = 2d * Math.PI * i / EllipseSteps;
            var p = Map(
                matrix,
                cx + rx * (float)Math.Cos(angle),
                cy + ry * (float)Math.Sin(angle));

            if (i == 0)
                path.MoveTo(p);
            else
                path.LineTo(p);
        }

        path.Close();
    }

    private static Skia.SKPoint Map(Shim.SKMatrix matrix, float x, float y)
    {
        var mapped = matrix.MapPoint(new Shim.SKPoint(x, y));
        return new Skia.SKPoint(mapped.X, mapped.Y);
    }

    private static void WriteSvg(Skia.SKPath path, string outputPath)
    {
        var bounds = path.Bounds;
        var width = Math.Max(bounds.Width, 1e-6f);
        var height = Math.Max(bounds.Height, 1e-6f);
        var padding = Math.Max(width, height) * 0.02f;

        var viewport = new Skia.SKRect(
            bounds.Left - padding,
            bounds.Top - padding,
            bounds.Right + padding,
            bounds.Bottom + padding);

        using var stream = File.Create(outputPath);
        using var canvas = Skia.SKSvgCanvas.Create(viewport, stream);
        using var paint = new Skia.SKPaint
        {
            Style = Skia.SKPaintStyle.Fill,
            Color = Skia.SKColors.Black,
            IsAntialias = true
        };

        canvas.DrawPath(path, paint);
        canvas.Flush();
    }
}

public sealed record NormalizedShapeResult(
    string OutputPath,
    double Width,
    double Height);
