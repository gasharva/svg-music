using SvgStructure.Models;
using Svg.Skia;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

public sealed class PrimitiveResolver
{
    private readonly PathContourSplitter _contourSplitter = new();
    private readonly PrimitiveContourExtractor _contourExtractor = new();
    private readonly CompetitivePrimitiveClassifier _classifier;
    private readonly GarbageCleaner _garbageCleaner = new();
    private readonly StaffLinePrimitiveDetector _staffLineDetector = new();

    public PrimitiveResolver(double proximityPercentOfMeasureHeight = 0.25)
    {
        _classifier = new CompetitivePrimitiveClassifier
        {
            ProximityPercentOfMeasureHeight = proximityPercentOfMeasureHeight
        };
    }

    public PrimitiveResolution Resolve(PartMeasureResolution structure)
    {
        using var svg = SKSvg.CreateFromFile(structure.SvgPath);
        var model = svg.Model
            ?? throw new InvalidOperationException("Svg.Skia did not produce a retained scene model.");

        var workingModel = model.DeepClone();
        SplitContours(workingModel);

        var raw = new List<RawPrimitive>();
        CollectPrimitives(workingModel, Shim.SKMatrix.Identity, raw, currentUseKey: null);

        // Keep the complete geometry of every <use> before staff-line/garbage filtering. If one
        // surviving primitive belongs to a use, diagnostics can export that use exactly once with
        // all of its contours rather than exporting an arbitrary contour fragment.
        var sourceUseContours = raw
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceUseKey))
            .GroupBy(x => x.SourceUseKey!, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<PrimitiveContour>)x.Select(p => p.Contour).ToArray(),
                StringComparer.Ordinal);

        var regions = structure.Map.Blocks
            .Select(x => new StaffMeasureRegion(
                x.MeasureNumber, x.SystemIndex, x.PartNumber - 1,
                x.PhysicalBounds.Left, x.PhysicalBounds.Right,
                x.PhysicalBounds.Top, x.PhysicalBounds.Bottom))
            .ToArray();

        var staffLineIds = _staffLineDetector.Detect(raw, regions);
        var content = raw.Where(x => !staffLineIds.Contains(x.Id)).ToArray();
        var cleanup = _garbageCleaner.Clean(content, regions);
        var claims = _classifier.Classify(cleanup.Primitives, regions, structure.Map.PageBounds);

        var resolved = cleanup.Primitives
            .Select(primitive => ResolvePrimitive(primitive, structure, claims, sourceUseContours))
            .ToArray();

        return new PrimitiveResolution(structure, resolved);
    }

    private static ResolvedPrimitive ResolvePrimitive(
        RawPrimitive primitive,
        PartMeasureResolution structure,
        IReadOnlyDictionary<int, HashSet<StaffMeasureKey>> claims,
        IReadOnlyDictionary<string, IReadOnlyList<PrimitiveContour>> sourceUseContours)
    {
        var allUseContours = primitive.SourceUseKey is not null &&
                             sourceUseContours.TryGetValue(primitive.SourceUseKey, out var useContours)
            ? useContours
            : null;

        if (claims.TryGetValue(primitive.Id, out var keys) && keys.Count == 1)
        {
            var key = keys.Single();
            return new ResolvedPrimitive(
                primitive.Id, primitive.Bounds, primitive.Contour,
                PrimitiveLogicalScope.PartMeasure, key.PartIndex + 1, key.MeasureNumber,
                primitive.SourceUseKey, allUseContours);
        }

        var measureNumber = ResolveNearestMeasure(primitive.Bounds, structure.Map);
        if (measureNumber is not null)
        {
            return new ResolvedPrimitive(
                primitive.Id, primitive.Bounds, primitive.Contour,
                PrimitiveLogicalScope.Measure, null, measureNumber,
                primitive.SourceUseKey, allUseContours);
        }

        return new ResolvedPrimitive(
            primitive.Id, primitive.Bounds, primitive.Contour,
            PrimitiveLogicalScope.PhysicalOnly, null, null,
            primitive.SourceUseKey, allUseContours);
    }

    private static int? ResolveNearestMeasure(RectD bounds, PartMeasureMap map)
    {
        if (map.Blocks.Count == 0) return null;
        var measureBounds = map.Blocks.GroupBy(x => x.MeasureNumber).Select(group => new
        {
            MeasureNumber = group.Key,
            Bounds = new RectD(
                group.Min(x => x.PhysicalBounds.Left), group.Min(x => x.PhysicalBounds.Top),
                group.Max(x => x.PhysicalBounds.Right), group.Max(x => x.PhysicalBounds.Bottom))
        }).ToArray();

        var horizontal = measureBounds
            .Where(x => bounds.CenterX >= x.Bounds.Left && bounds.CenterX <= x.Bounds.Right)
            .ToArray();
        var candidates = horizontal.Length > 0 ? horizontal : measureBounds;
        return candidates.OrderBy(x => RectangleDistance(bounds, x.Bounds))
            .ThenBy(x => Math.Abs(bounds.CenterX - x.Bounds.CenterX))
            .Select(x => (int?)x.MeasureNumber).FirstOrDefault();
    }

    private static double RectangleDistance(RectD a, RectD b)
    {
        var dx = a.Right < b.Left ? b.Left - a.Right : b.Right < a.Left ? a.Left - b.Right : 0;
        var dy = a.Bottom < b.Top ? b.Top - a.Bottom : b.Bottom < a.Top ? a.Top - b.Bottom : 0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void SplitContours(Shim.SKPicture picture)
    {
        if (picture.Commands is null) return;
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
        foreach (var command in rebuilt) picture.Commands.Add(command);
    }

    private void CollectPrimitives(
        Shim.SKPicture picture,
        Shim.SKMatrix parentMatrix,
        ICollection<RawPrimitive> primitives,
        string? currentUseKey)
    {
        if (picture.Commands is null) return;
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
                    if (stack.Count > 0) matrix = stack.Pop();
                    break;
                case Shim.SetMatrixCanvasCommand setMatrix:
                    matrix = parentMatrix.PreConcat(setMatrix.TotalMatrix);
                    break;
                case Shim.DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                {
                    var mappedBounds = matrix.MapRect(drawPath.Path.Bounds);
                    var points = _contourExtractor.Extract(drawPath.Path, matrix);
                    if (points.Count < 2) break;
                    var sourceUseKey = currentUseKey ?? GetUseKey(drawPath);
                    primitives.Add(new RawPrimitive(
                        primitives.Count,
                        new RectD(mappedBounds.Left, mappedBounds.Top, mappedBounds.Right, mappedBounds.Bottom),
                        new PrimitiveContour(points),
                        sourceUseKey));
                    break;
                }
                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                {
                    var nestedUseKey = GetUseKey(drawPicture) ?? currentUseKey;
                    CollectPrimitives(drawPicture.Picture, matrix, primitives, nestedUseKey);
                    break;
                }
            }
        }
    }

    private static string? GetUseKey(Shim.CanvasCommand command)
    {
        var typeName = command.SourceElementTypeName;
        var address = command.SourceElementAddress;
        var isUse = string.Equals(typeName, "use", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(address) &&
                     address.Contains("use", StringComparison.OrdinalIgnoreCase));
        if (!isUse)
            return null;

        if (!string.IsNullOrWhiteSpace(address))
            return address;
        if (!string.IsNullOrWhiteSpace(command.SourceElementId))
            return "use#" + command.SourceElementId;
        return null;
    }
}
