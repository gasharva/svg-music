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
        CollectPrimitives(
            workingModel,
            Shim.SKMatrix.Identity,
            raw,
            scenePath: "scene",
            currentGroupAnchor: null,
            inheritedExplicitUse: false);

        // Capture complete geometry for every retained picture/source group before any filtering.
        // Svg.Skia frequently expands an SVG <use> into a DrawPicture whose children are DrawPath
        // commands and no longer labels those child paths as "use". Grouping by the nearest picture
        // instance therefore preserves the original glyph instance even when the explicit <use>
        // marker was lost during SVG parsing.
        var sourceGroupContours = raw
            .Where(x => !string.IsNullOrWhiteSpace(x.Source.GroupAnchor))
            .GroupBy(x => x.Source.GroupAnchor!, StringComparer.Ordinal)
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
        var allGroupContours = primitive.Source.GroupAnchor is not null &&
                               sourceGroupContours.TryGetValue(primitive.Source.GroupAnchor, out var groupContours)
            ? groupContours
            : null;

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
        string scenePath,
        string? currentGroupAnchor,
        bool inheritedExplicitUse)
    {
        if (picture.Commands is null) return;
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

                    var anchor = SourceAnchor(drawPath, commandPath + "/path");
                    var explicitUse = inheritedExplicitUse || IsExplicitUse(drawPath);
                    var source = new PrimitiveSourceRef(
                        anchor,
                        currentGroupAnchor,
                        drawPath.SourceElementTypeName,
                        drawPath.SourceElementId,
                        drawPath.SourceElementAddress,
                        explicitUse);

                    primitives.Add(new RawPrimitive(
                        primitives.Count,
                        new RectD(mappedBounds.Left, mappedBounds.Top, mappedBounds.Right, mappedBounds.Bottom),
                        new PrimitiveContour(points),
                        source));
                    break;
                }
                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                {
                    // A retained picture is the strongest surviving instance boundary in Svg.Skia.
                    // Prefer the original XML-ish address/id when exposed; otherwise the deterministic
                    // scene path remains a stable anchor for this exact SVG and parser version.
                    var pictureAnchor = SourceAnchor(drawPicture, commandPath + "/picture");
                    var explicitUse = inheritedExplicitUse || IsExplicitUse(drawPicture);
                    CollectPrimitives(
                        drawPicture.Picture,
                        matrix,
                        primitives,
                        scenePath: pictureAnchor,
                        currentGroupAnchor: pictureAnchor,
                        inheritedExplicitUse: explicitUse);
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

    private static bool IsExplicitUse(Shim.CanvasCommand command)
    {
        if (string.Equals(command.SourceElementTypeName, "use", StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrWhiteSpace(command.SourceElementAddress) &&
               command.SourceElementAddress.Contains("use", StringComparison.OrdinalIgnoreCase);
    }
}
