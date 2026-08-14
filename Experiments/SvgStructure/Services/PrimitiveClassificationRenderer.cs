using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

public sealed class PrimitiveClassificationRenderer
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

    private static readonly (byte R, byte G, byte B) UnclassifiedColor = (110, 110, 110);

    private readonly PathContourSplitter _contourSplitter = new();
    private readonly RawPrimitiveDetector _detector;
    private readonly GarbageCleaner _garbageCleaner = new();
    private readonly StaffLinePrimitiveDetector _staffLineDetector = new();

    public PrimitiveClassificationRenderer(double proximityPercentOfMeasureHeight = 0.18)
    {
        _detector = new RawPrimitiveDetector
        {
            ProximityPercentOfMeasureHeight = proximityPercentOfMeasureHeight
        };
    }

    public string Render(
        string svgPath,
        IReadOnlyList<StaffSystem> systems,
        string? outputPath = null)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var model = svg.Model
            ?? throw new InvalidOperationException("Svg.Skia did not produce a retained scene model.");

        var classifiedModel = model.DeepClone();
        SplitContours(classifiedModel);

        var primitiveCommands = new Dictionary<int, Shim.DrawPathCanvasCommand>();
        var primitives = new List<RawPrimitive>();
        CollectPrimitives(classifiedModel, Shim.SKMatrix.Identity, primitiveCommands, primitives);

        var regions = BuildStaffMeasureRegions(systems);

        var staffLineIds = _staffLineDetector.Detect(primitives, regions);
        var musicalPrimitives = primitives
            .Where(x => !staffLineIds.Contains(x.Id))
            .ToList();

        var cleanup = _garbageCleaner.Clean(musicalPrimitives, regions);
        var pageBounds = ToRectD(model.CullRect);
        var claims = BuildClaims(regions, cleanup.Primitives, pageBounds);

        var keepOriginalIds = staffLineIds
            .Concat(cleanup.GarbageIds)
            .ToHashSet();

        ApplyClassification(primitiveCommands, claims, keepOriginalIds);

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
        DrawStaffMeasureBorders(canvas, regions);

        outputPath ??= Path.Combine(
            Path.GetDirectoryName(svgPath) ?? ".",
            $"{Path.GetFileNameWithoutExtension(svgPath)}.classified.png");

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);

        return outputPath;
    }

    private Dictionary<int, HashSet<StaffMeasureKey>> BuildClaims(
        IReadOnlyList<StaffMeasureRegion> regions,
        IReadOnlyList<RawPrimitive> primitives,
        RectD pageBounds)
    {
        // First establish hard anchors: a primitive that physically intersects exactly one
        // staff-measure belongs there before any propagation starts. Other staff detectors
        // are not allowed to use such a primitive as a bridge into their own cluster.
        var directClaims = primitives.ToDictionary(
            p => p.Id,
            p => regions
                .Where(r => p.Bounds.Intersects(r.Bounds))
                .Select(r => r.Key)
                .Distinct()
                .ToHashSet());

        var propagatedClaims = new Dictionary<int, HashSet<StaffMeasureKey>>();

        foreach (var region in regions)
        {
            var blocked = directClaims
                .Where(x => x.Value.Count == 1 && !x.Value.Contains(region.Key))
                .Select(x => x.Key)
                .ToHashSet();

            var (topLimit, bottomLimit) = GetVerticalLimits(region, regions, pageBounds);
            var detected = _detector.Detect(
                region,
                primitives,
                topLimit,
                bottomLimit,
                blocked);

            foreach (var primitiveId in detected)
                AddClaim(propagatedClaims, primitiveId, region.Key);
        }

        // Hard anchors win over propagated claims. Ambiguous direct intersections remain
        // ambiguous and therefore render gray, which is useful diagnostic information.
        foreach (var (primitiveId, direct) in directClaims)
        {
            if (direct.Count > 0)
                propagatedClaims[primitiveId] = direct;
        }

        return propagatedClaims;
    }

    private static void AddClaim(
        IDictionary<int, HashSet<StaffMeasureKey>> claims,
        int primitiveId,
        StaffMeasureKey key)
    {
        if (!claims.TryGetValue(primitiveId, out var regionKeys))
        {
            regionKeys = new HashSet<StaffMeasureKey>();
            claims[primitiveId] = regionKeys;
        }

        regionKeys.Add(key);
    }

    private static (double Top, double Bottom) GetVerticalLimits(
        StaffMeasureRegion region,
        IReadOnlyList<StaffMeasureRegion> regions,
        RectD pageBounds)
    {
        var above = regions
            .Where(x => x.Key != region.Key || x.SystemIndex != region.SystemIndex)
            .Where(x => HorizontallyOverlaps(x, region))
            .Where(x => x.Bottom <= region.Top)
            .OrderByDescending(x => x.Bottom)
            .FirstOrDefault();

        var below = regions
            .Where(x => x.Key != region.Key || x.SystemIndex != region.SystemIndex)
            .Where(x => HorizontallyOverlaps(x, region))
            .Where(x => x.Top >= region.Bottom)
            .OrderBy(x => x.Top)
            .FirstOrDefault();

        return (
            above?.Bottom ?? pageBounds.Top,
            below?.Top ?? pageBounds.Bottom);
    }

    private static bool HorizontallyOverlaps(StaffMeasureRegion a, StaffMeasureRegion b) =>
        a.Right > b.Left && a.Left < b.Right;

    private static void ApplyClassification(
        IReadOnlyDictionary<int, Shim.DrawPathCanvasCommand> commands,
        IReadOnlyDictionary<int, HashSet<StaffMeasureKey>> claims,
        IReadOnlySet<int> keepOriginalIds)
    {
        foreach (var (primitiveId, command) in commands)
        {
            if (command.Paint is null)
                continue;

            if (keepOriginalIds.Contains(primitiveId))
                continue;

            var color = claims.TryGetValue(primitiveId, out var regionKeys) && regionKeys.Count == 1
                ? GetColor(regionKeys.Single())
                : UnclassifiedColor;

            command.Paint.Color = new Shim.SKColor(color.R, color.G, color.B, 255);
            command.Paint.Shader = null;
        }
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

    private static void CollectPrimitives(
        Shim.SKPicture picture,
        Shim.SKMatrix parentMatrix,
        IDictionary<int, Shim.DrawPathCanvasCommand> commands,
        ICollection<RawPrimitive> primitives)
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
                    var id = primitives.Count;
                    var bounds = matrix.MapRect(drawPath.Path.Bounds);
                    primitives.Add(new RawPrimitive(id, ToRectD(bounds)));
                    commands[id] = drawPath;
                    break;
                }

                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    CollectPrimitives(drawPicture.Picture, matrix, commands, primitives);
                    break;
            }
        }
    }

    private static IReadOnlyList<StaffMeasureRegion> BuildStaffMeasureRegions(
        IReadOnlyList<StaffSystem> systems)
    {
        var result = new List<StaffMeasureRegion>();
        var measureNumber = 1;

        for (var systemIndex = 0; systemIndex < systems.Count; systemIndex++)
        {
            var system = systems[systemIndex];

            for (var measureIndex = 0; measureIndex < system.BarXs.Count - 1; measureIndex++, measureNumber++)
            {
                foreach (var staff in system.Staffs)
                {
                    result.Add(new StaffMeasureRegion(
                        measureNumber,
                        systemIndex,
                        staff.PartIndex,
                        system.BarXs[measureIndex],
                        system.BarXs[measureIndex + 1],
                        staff.Top,
                        staff.Bottom));
                }
            }
        }

        return result;
    }

    private static void DrawStaffMeasureBorders(
        SKCanvas canvas,
        IReadOnlyList<StaffMeasureRegion> regions)
    {
        foreach (var region in regions)
        {
            var color = GetColor(region.Key);
            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, 235),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.2f,
                IsAntialias = true
            };

            canvas.DrawRect(
                new SKRect(
                    (float)region.Left,
                    (float)region.Top,
                    (float)region.Right,
                    (float)region.Bottom),
                paint);
        }
    }

    private static (byte R, byte G, byte B) GetColor(StaffMeasureKey key)
    {
        var index = (key.MeasureNumber - 1 + key.PartIndex * 3) % Palette.Length;
        return Palette[index];
    }

    private static RectD ToRectD(Shim.SKRect rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
