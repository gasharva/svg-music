using SvgStructure.Models;
using Svg.Skia;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

public sealed class PrimitiveResolver
{
    private readonly PathContourSplitter _contourSplitter = new();
    private readonly PrimitiveContourExtractor _contourExtractor = new();
    private readonly SvgUseInstanceMapper _useInstanceMapper = new();
    private readonly CompetitivePrimitiveClassifier _classifier;
    private readonly GarbageCleaner _garbageCleaner = new();
    private readonly StaffLinePrimitiveDetector _staffLineDetector = new();

    public PrimitiveResolver(double proximityInStaffSpaces = 2.0)
    {
        _classifier = new CompetitivePrimitiveClassifier
        {
            ProximityInStaffSpaces = proximityInStaffSpaces
        };
    }

    public PrimitiveResolution Resolve(PartMeasureResolution structure)
    {
        using var svg = SKSvg.CreateFromFile(structure.SvgPath);
        var model = svg.Model
            ?? throw new InvalidOperationException("Svg.Skia did not produce a retained scene model.");

        var workingModel = model.DeepClone();
        SplitContours(workingModel);

        var collected = new List<RawPrimitive>();
        CollectPrimitives(
            workingModel,
            Shim.SKMatrix.Identity,
            collected,
            scenePath: "scene");

        // The retained model is flattened and its SourceElementAddress normally points at the
        // referenced definition, not at a concrete <use>. Reattach instance identity from the
        // semantic SourceDocument before any filtering so all later steps keep true provenance.
        var raw = _useInstanceMapper.Map(svg, collected).ToArray();

        // Group geometry only by a real <use> instance. Never fall back to SourceElementAddress:
        // several visual instances can legitimately share the same referenced source element.
        var groupedByUse = raw
            .Where(x => !string.IsNullOrWhiteSpace(x.Source.GroupAnchor))
            .GroupBy(x => x.Source.GroupAnchor!, StringComparer.Ordinal)
            .ToArray();

        var sourceGroupContours = groupedByUse
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

        var content = raw
            .Where(x => !staffLineIds.Contains(x.Id))
            .ToArray();

        var cleanup = _garbageCleaner.Clean(content, regions);
        var claims = _classifier.Classify(cleanup.Primitives, regions, structure.Map.PageBounds);

        var resolved = cleanup.Primitives
            .Select(primitive => ResolvePrimitive(primitive, structure, claims, sourceGroupContours))
            .ToArray();

        return new PrimitiveResolution(structure, resolved);
    }

    private static ResolvedPrimitive ResolvePrimitive(
        RawPrimitive primitive,
        PartMeasureResolution structure,
        IReadOnlyDictionary<int, HashSet<StaffMeasureKey>> claims,
        IReadOnlyDictionary<string, IReadOnlyList<PrimitiveContour>> sourceGroupContours)
    {
        IReadOnlyList<PrimitiveContour>? allGroupContours = null;
        if (!string.IsNullOrWhiteSpace(primitive.Source.GroupAnchor) &&
            sourceGroupContours.TryGetValue(primitive.Source.GroupAnchor!, out var groupContours))
        {
            allGroupContours = groupContours;
        }

        if (claims.TryGetValue(primitive.Id, out var keys) && keys.Count == 1)
        {
            var key = keys.Single();
            return new ResolvedPrimitive(
                primitive.Id, primitive.Bounds, primitive.Contour,
                PrimitiveLogicalScope.PartMeasure, key.PartIndex + 1, key.MeasureNumber,
                primitive.Source, allGroupContours);
        }

        var measureNumber = ResolveNearestMeasure(primitive.Bounds, structure.Map);
        if (measureNumber is not null)
        {
            return new ResolvedPrimitive(
                primitive.Id, primitive.Bounds, primitive.Contour,
                PrimitiveLogicalScope.Measure, null, measureNumber,
                primitive.Source, allGroupContours);
        }

        return new ResolvedPrimitive(
            primitive.Id, primitive.Bounds, primitive.Contour,
            PrimitiveLogicalScope.PhysicalOnly, null, null,
            primitive.Source, allGroupContours);
    }

    private static int? ResolveNearestMeasure(RectD bounds, PartMeasureMap map)
    {
        if (map.Blocks.Count == 0)
            return null;

        var measureGroups = map.Blocks
            .GroupBy(x => x.MeasureNumber)
            .ToArray();

        var measureBounds = measureGroups
            .Select(group => new
            {
                MeasureNumber = group.Key,
                Bounds = new RectD(
                    group.Min(x => x.PhysicalBounds.Left),
                    group.Min(x => x.PhysicalBounds.Top),
                    group.Max(x => x.PhysicalBounds.Right),
                    group.Max(x => x.PhysicalBounds.Bottom))
            })
            .ToArray();

        var horizontallyContaining = measureBounds
            .Where(x => bounds.CenterX >= x.Bounds.Left && bounds.CenterX <= x.Bounds.Right)
            .ToArray();

        var candidates = horizontallyContaining.Length > 0
            ? horizontallyContaining
            : measureBounds;

        var nearest = candidates
            .OrderBy(x => RectangleDistance(bounds, x.Bounds))
            .ThenBy(x => Math.Abs(bounds.CenterX - x.Bounds.CenterX))
            .FirstOrDefault();

        return nearest?.MeasureNumber;
    }

    private static double RectangleDistance(RectD a, RectD b)
    {
        var dx = a.Right < b.Left
            ? b.Left - a.Right
            : b.Right < a.Left
                ? a.Left - b.Right
                : 0;

        var dy = a.Bottom < b.Top
            ? b.Top - a.Bottom
            : b.Bottom < a.Top
                ? a.Top - b.Bottom
                : 0;

        return Math.Sqrt(dx * dx + dy * dy);
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

    private void CollectPrimitives(
        Shim.SKPicture picture,
        Shim.SKMatrix parentMatrix,
        ICollection<RawPrimitive> primitives,
        string scenePath)
    {
        if (picture.Commands is null)
            return;

        var matrix = parentMatrix;
        var stack = new Stack<Shim.SKMatrix>();

        for (var commandIndex = 0; commandIndex < picture.Commands.Count; commandIndex++)
        {
            var command = picture.Commands[commandIndex];
            var commandPath = $"{scenePath}/command[{commandIndex}]";

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
                    var mappedBounds = matrix.MapRect(drawPath.Path.Bounds);
                    var points = _contourExtractor.Extract(drawPath.Path, matrix);
                    if (points.Count < 2)
                        break;

                    var source = new PrimitiveSourceRef(
                        SourceAnchor(drawPath, commandPath + "/path"),
                        null,
                        drawPath.SourceElementTypeName,
                        drawPath.SourceElementId,
                        drawPath.SourceElementAddress,
                        false);

                    primitives.Add(new RawPrimitive(
                        primitives.Count,
                        new RectD(mappedBounds.Left, mappedBounds.Top, mappedBounds.Right, mappedBounds.Bottom),
                        new PrimitiveContour(points),
                        source));
                    break;
                }

                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                {
                    var pictureAnchor = SourceAnchor(drawPicture, commandPath + "/picture");
                    CollectPrimitives(
                        drawPicture.Picture,
                        matrix,
                        primitives,
                        scenePath: pictureAnchor);
                    break;
                }
            }
        }
    }

    private static string SourceAnchor(Shim.CanvasCommand command, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(command.SourceElementAddress))
            return "xml:" + command.SourceElementAddress;

        if (!string.IsNullOrWhiteSpace(command.SourceElementId))
            return "id:" + command.SourceElementId;

        return fallback;
    }
}
