using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

public sealed class MeasureOverlayRenderer
{
    private const float RenderScale = 2f;

    // Deliberately loud diagnostic colors. The index formula below makes both horizontal
    // neighbours (measures) and vertical neighbours (parts) use different colors.
    private static readonly (byte R, byte G, byte B)[] Palette =
    {
        (220, 20, 60),    // crimson
        (0, 120, 215),    // blue
        (0, 150, 90),     // green
        (230, 120, 0),    // orange
        (145, 55, 190),   // purple
        (0, 160, 180),    // cyan
        (210, 45, 145),   // magenta
        (105, 125, 0)     // olive
    };

    private readonly PrimitiveClassifier _classifier = new();

    public string Render(
        string svgPath,
        IReadOnlyList<StaffSystem> systems,
        string? outputPath = null)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var model = svg.Model
            ?? throw new InvalidOperationException("Svg.Skia did not produce a retained scene model.");

        // Work on a clone: the original model remains untouched and can still be rendered
        // normally. At this stage one DrawPath command is our definition of an SVG primitive.
        var classifiedModel = model.DeepClone();
        RecolorPicture(classifiedModel, Shim.SKMatrix.Identity, systems);

        using var picture = svg.SkiaModel.ToSKPicture(classifiedModel)
            ?? throw new InvalidOperationException("Svg.Skia could not render the classified scene model.");

        var bounds = picture.CullRect;
        var width = (int)Math.Ceiling(bounds.Width * RenderScale);
        var height = (int)Math.Ceiling(bounds.Height * RenderScale);

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);
        canvas.Scale(RenderScale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);

        DrawClassificationCells(canvas, systems);

        outputPath ??= Path.Combine(
            Path.GetDirectoryName(svgPath) ?? ".",
            $"{Path.GetFileNameWithoutExtension(svgPath)}.classified.png");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);

        return outputPath;
    }

    private void RecolorPicture(
        Shim.SKPicture picture,
        Shim.SKMatrix parentMatrix,
        IReadOnlyList<StaffSystem> systems)
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
                    var bounds = matrix.MapRect(drawPath.Path.Bounds);
                    var assignment = _classifier.Classify(
                        (bounds.Left + bounds.Right) / 2,
                        (bounds.Top + bounds.Bottom) / 2,
                        systems);

                    if (assignment is not null && drawPath.Paint is not null)
                        ApplyColor(drawPath.Paint, GetColor(assignment.PartIndex, assignment.MeasureNumber));

                    break;
                }

                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    RecolorPicture(drawPicture.Picture, matrix, systems);
                    break;
            }
        }
    }

    private static void ApplyColor(Shim.SKPaint paint, (byte R, byte G, byte B) color)
    {
        paint.Color = new Shim.SKColor(color.R, color.G, color.B, 255);

        // There are no gradients in the musical geometry of the current sample. Clearing a
        // shader makes the diagnostic color authoritative if a future SVG happens to use one.
        paint.Shader = null;
    }

    private static void DrawClassificationCells(SKCanvas canvas, IReadOnlyList<StaffSystem> systems)
    {
        var measureNumber = 1;

        foreach (var system in systems)
        {
            for (var measureIndex = 0; measureIndex < system.BarXs.Count - 1; measureIndex++, measureNumber++)
            {
                for (var partIndex = 0; partIndex < system.Staffs.Count; partIndex++)
                {
                    var color = GetColor(partIndex, measureNumber);
                    using var paint = new SKPaint
                    {
                        Color = new SKColor(color.R, color.G, color.B, 230),
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = 1.3f,
                        IsAntialias = true
                    };

                    var (top, bottom) = GetPartVerticalArea(system, partIndex);
                    var rect = new SKRect(
                        (float)system.BarXs[measureIndex],
                        (float)top,
                        (float)system.BarXs[measureIndex + 1],
                        (float)bottom);

                    canvas.DrawRect(rect, paint);
                }
            }
        }
    }

    private static (double Top, double Bottom) GetPartVerticalArea(StaffSystem system, int partIndex)
    {
        var staff = system.Staffs[partIndex];
        var staffHeight = Math.Max(1, staff.Bottom - staff.Top);
        var halo = Math.Max(12, staffHeight * 2.2);

        var top = partIndex == 0
            ? system.Top - halo
            : (system.Staffs[partIndex - 1].Bottom + staff.Top) / 2;

        var bottom = partIndex == system.Staffs.Count - 1
            ? system.Bottom + halo
            : (staff.Bottom + system.Staffs[partIndex + 1].Top) / 2;

        return (top, bottom);
    }

    private static (byte R, byte G, byte B) GetColor(int partIndex, int measureNumber)
    {
        var index = (measureNumber - 1 + partIndex * 3) % Palette.Length;
        return Palette[index];
    }
}
