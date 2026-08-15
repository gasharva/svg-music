using System.Globalization;
using System.Security;
using ShimSkiaSharp;
using Svg.Skia;

namespace SvgSymbols.Services;

/// <summary>
/// Converts the renderer's many overlapping SVG paths into the minimal filled silhouette
/// that Skia PathOps sees on screen. No rasterization is involved.
/// </summary>
public sealed class SvgShapeNormalizer
{
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

    public SKPath Normalize(string sourcePath)
    {
        using var svg = SKSvg.CreateFromFile(sourcePath);
        var picture = svg.Model
            ?? throw new InvalidOperationException(
                $"Svg.Skia did not produce a retained scene model for '{sourcePath}'.");

        SKPath? merged = null;
        try
        {
            ReadPicture(picture, SKMatrix.Identity, path =>
            {
                using var transformed = new SKPath();
                path.Transform(SKMatrix.Identity, transformed);

                if (merged is null)
                {
                    merged = new SKPath();
                    merged.AddPath(transformed);
                    return;
                }

                var union = merged.Op(transformed, SKPathOp.Union);
                if (union is null)
                    throw new InvalidOperationException("Skia PathOps union failed while normalizing SVG.");

                merged.Dispose();
                merged = union;
            });

            if (merged is null || merged.IsEmpty)
                throw new InvalidOperationException($"No drawable paths found in '{sourcePath}'.");

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

    private static void ReadPicture(
        SKPicture picture,
        SKMatrix parentMatrix,
        Action<SKPath> onPath)
    {
        if (picture.Commands is null)
            return;

        var matrix = parentMatrix;
        var stack = new Stack<SKMatrix>();

        foreach (var command in picture.Commands)
        {
            switch (command)
            {
                case SaveCanvasCommand:
                case SaveLayerCanvasCommand:
                    stack.Push(matrix);
                    break;

                case RestoreCanvasCommand:
                    if (stack.Count > 0)
                        matrix = stack.Pop();
                    break;

                case SetMatrixCanvasCommand setMatrix:
                    matrix = parentMatrix.PreConcat(setMatrix.TotalMatrix);
                    break;

                case DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                {
                    using var transformed = new SKPath();
                    drawPath.Path.Transform(matrix, transformed);
                    onPath(transformed);
                    break;
                }

                case DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    ReadPicture(drawPicture.Picture, matrix, onPath);
                    break;
            }
        }
    }

    private static void WriteSvg(SKPath path, string outputPath)
    {
        var bounds = path.Bounds;
        var width = Math.Max(bounds.Width, 1e-6f);
        var height = Math.Max(bounds.Height, 1e-6f);
        var padding = Math.Max(width, height) * 0.02f;

        var left = bounds.Left - padding;
        var top = bounds.Top - padding;
        var viewWidth = width + 2f * padding;
        var viewHeight = height + 2f * padding;
        var data = SecurityElement.Escape(path.ToSvgPathData()) ?? string.Empty;

        var fillRule = path.FillType is SKPathFillType.EvenOdd or SKPathFillType.InverseEvenOdd
            ? "evenodd"
            : "nonzero";

        var svg =
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{Fmt(left)} {Fmt(top)} {Fmt(viewWidth)} {Fmt(viewHeight)}\">" +
            $"<path d=\"{data}\" fill=\"black\" fill-rule=\"{fillRule}\"/>" +
            "</svg>";

        File.WriteAllText(outputPath, svg);
    }

    private static string Fmt(double value) =>
        value.ToString("0.#####", CultureInfo.InvariantCulture);
}

public sealed record NormalizedShapeResult(
    string OutputPath,
    double Width,
    double Height);
