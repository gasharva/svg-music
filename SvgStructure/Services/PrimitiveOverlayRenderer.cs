using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

/// <summary>Diagnostic only. Visualizes logical ownership produced by <see cref="PrimitiveResolver"/>.</summary>
public sealed class PrimitiveOverlayRenderer
{
    private const float RenderScale = 2f;

    private static readonly (byte R, byte G, byte B)[] Palette =
    {
        (220, 20, 60),
        (0, 120, 215),
        (0, 150, 90),
        (230, 120, 0),
        (145, 55, 190),
        (0, 160, 180),
        (210, 45, 145),
        (105, 125, 0)
    };

    private static readonly (byte R, byte G, byte B) PhysicalOnlyColor = (110, 110, 110);
    private static readonly (byte R, byte G, byte B) MeasureOnlyColor = (40, 40, 40);

    private readonly PathContourSplitter _contourSplitter = new();

    public string Render(PrimitiveResolution resolution, string outputPath)
    {
        using var svg = SKSvg.CreateFromFile(resolution.Structure.SvgPath);
        var model = svg.Model
            ?? throw new InvalidOperationException("Svg.Skia did not produce a retained scene model.");

        var classifiedModel = model.DeepClone();
        SplitContours(classifiedModel);

        var commands = new Dictionary<int, Shim.DrawPathCanvasCommand>();
        CollectCommands(classifiedModel, commands);
        ApplyResolution(commands, resolution.Primitives);

        using var picture = svg.SkiaModel.ToSKPicture(classifiedModel)
            ?? throw new InvalidOperationException("Svg.Skia could not render the classified scene model.");

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
        DrawBlockBorders(canvas, resolution.Structure.Map.Blocks);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return outputPath;
    }

    private static void ApplyResolution(
        IReadOnlyDictionary<int, Shim.DrawPathCanvasCommand> commands,
        IReadOnlyList<ResolvedPrimitive> primitives)
    {
        foreach (var primitive in primitives)
        {
            if (!commands.TryGetValue(primitive.Id, out var command) || command.Paint is null)
                continue;

            // Keep staff lines and deliberately rejected giant garbage untouched: they are not
            // logical recognition output, only physical SVG infrastructure/noise.
            if (primitive.Kind is PrimitiveKind.StaffLine or PrimitiveKind.Garbage)
                continue;

            var color = primitive.Scope switch
            {
                PrimitiveLogicalScope.PartMeasure => GetBlockColor(
                    primitive.PartNumber!.Value,
                    primitive.MeasureNumber!.Value),
                PrimitiveLogicalScope.Measure => MeasureOnlyColor,
                _ => PhysicalOnlyColor
            };

            command.Paint.Color = new Shim.SKColor(color.R, color.G, color.B, 255);
            command.Paint.Shader = null;
        }
    }

    private static void DrawBlockBorders(SKCanvas canvas, IReadOnlyList<PartMeasureBlock> blocks)
    {
        foreach (var block in blocks)
        {
            var color = GetBlockColor(block.PartNumber, block.MeasureNumber);
            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, 235),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f,
                IsAntialias = true
            };

            var b = block.PhysicalBounds;
            canvas.DrawRect(new SKRect((float)b.Left, (float)b.Top, (float)b.Right, (float)b.Bottom), paint);
        }
    }

    private static (byte R, byte G, byte B) GetBlockColor(int partNumber, int measureNumber)
    {
        var index = (measureNumber - 1 + (partNumber - 1) * 3) % Palette.Length;
        return Palette[index];
    }

    private void SplitContours(Shim.SKPicture picture)
    {
        if (picture.Commands is null)
            return;

        var rebuilt = new List<Shim.CanvasCommand>();
        foreach (var command in picture.Commands)
        {
            switch (command)
            {
                case Shim.DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                    foreach (var contour in _contourSplitter.Split(drawPath.Path))
                    {
                        rebuilt.Add(new Shim.DrawPathCanvasCommand(contour, drawPath.Paint?.DeepClone())
                        {
                            SourceElementId = drawPath.SourceElementId,
                            SourceElementAddress = drawPath.SourceElementAddress,
                            SourceElementTypeName = drawPath.SourceElementTypeName
                        });
                    }
                    break;

                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    SplitContours(drawPicture.Picture);
                    rebuilt.Add(command);
                    break;

                default:
                    rebuilt.Add(command);
                    break;
            }
        }

        picture.Commands.Clear();
        foreach (var command in rebuilt)
            picture.Commands.Add(command);
    }

    private static void CollectCommands(
        Shim.SKPicture picture,
        IDictionary<int, Shim.DrawPathCanvasCommand> commands)
    {
        if (picture.Commands is null)
            return;

        foreach (var command in picture.Commands)
        {
            switch (command)
            {
                case Shim.DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                    commands[commands.Count] = drawPath;
                    break;
                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    CollectCommands(drawPicture.Picture, commands);
                    break;
            }
        }
    }
}
