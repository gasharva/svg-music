using SvgStructure.Models;
using Svg.Skia;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

/// <summary>
/// Pipeline step 2. Resolves raw SVG primitives against the logical map produced by
/// <see cref="PartMeasureResolver"/>.
///
/// A primitive may belong to one Pn-Mm block, to a measure as a whole (part is unknown / shared),
/// or remain physical-only when no unambiguous logical coordinate can be assigned.
/// </summary>
public sealed class PrimitiveResolver
{
    private readonly PathContourSplitter _contourSplitter = new();
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
        CollectPrimitives(workingModel, Shim.SKMatrix.Identity, raw);

        var regions = structure.Map.Blocks
            .Select(x => new StaffMeasureRegion(
                x.MeasureNumber,
                x.SystemIndex,
                x.PartNumber - 1,
                x.PhysicalBounds.Left,
                x.PhysicalBounds.Right,
                x.PhysicalBounds.Top,
                x.PhysicalBounds.Bottom))
            .ToArray();

        var staffLineIds = _staffLineDetector.Detect(raw, regions);
        var classifiable = raw.Where(x => !staffLineIds.Contains(x.Id)).ToArray();
        var cleanup = _garbageCleaner.Clean(classifiable, regions);
        var claims = _classifier.Classify(cleanup.Primitives, regions, structure.Map.PageBounds);
        var garbageIds = cleanup.GarbageIds.ToHashSet();

        var resolved = raw
            .Select(primitive => ResolvePrimitive(
                primitive,
                structure,
                claims,
                staffLineIds,
                garbageIds))
            .ToArray();

        return new PrimitiveResolution(structure, resolved);
    }

    private static ResolvedPrimitive ResolvePrimitive(
        RawPrimitive primitive,
        PartMeasureResolution structure,
        IReadOnlyDictionary<int, HashSet<StaffMeasureKey>> claims,
        IReadOnlySet<int> staffLineIds,
        IReadOnlySet<int> garbageIds)
    {
        // Staff lines are algorithmic anchors and garbage is deliberately excluded from ownership.
        if (staffLineIds.Contains(primitive.Id) || garbageIds.Contains(primitive.Id))
            return PhysicalOnly(primitive);

        if (claims.TryGetValue(primitive.Id, out var keys) && keys.Count == 1)
        {
            var key = keys.Single();
            return new ResolvedPrimitive(
                primitive.Id,
                primitive.Bounds,
                PrimitiveLogicalScope.PartMeasure,
                key.PartIndex + 1,
                key.MeasureNumber);
        }

        var sharedMeasure = TryResolveMeasureOnly(primitive.Bounds, structure.Map);
        if (sharedMeasure is not null)
        {
            return new ResolvedPrimitive(
                primitive.Id,
                primitive.Bounds,
                PrimitiveLogicalScope.Measure,
                null,
                sharedMeasure);
        }

        return PhysicalOnly(primitive);
    }

    /// <summary>
    /// A still-unclassified primitive is measure-wide only when it belongs horizontally to one
    /// measure and intersects/spans a vertical gap between adjacent parts of that measure.
    /// This captures cross-staff geometry without pretending that it belongs to either staff.
    /// </summary>
    private static int? TryResolveMeasureOnly(RectD bounds, PartMeasureMap map)
    {
        var candidateMeasures = map.Blocks
            .Where(x => bounds.IntersectsHorizontally(x.PhysicalBounds.Left, x.PhysicalBounds.Right))
            .Select(x => x.MeasureNumber)
            .Distinct()
            .ToArray();

        if (candidateMeasures.Length != 1)
            return null;

        var measureNumber = candidateMeasures[0];
        var blocks = map.GetMeasureBlocks(measureNumber)
            .OrderBy(x => x.PhysicalBounds.Top)
            .ToArray();

        for (var i = 0; i < blocks.Length - 1; i++)
        {
            var gapTop = blocks[i].PhysicalBounds.Bottom;
            var gapBottom = blocks[i + 1].PhysicalBounds.Top;
            if (gapBottom < gapTop)
                (gapTop, gapBottom) = (gapBottom, gapTop);

            if (bounds.Bottom >= gapTop && bounds.Top <= gapBottom)
                return measureNumber;
        }

        return null;
    }

    private static ResolvedPrimitive PhysicalOnly(RawPrimitive primitive) =>
        new(
            primitive.Id,
            primitive.Bounds,
            PrimitiveLogicalScope.PhysicalOnly,
            null,
            null);

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
                    var bounds = matrix.MapRect(drawPath.Path.Bounds);
                    primitives.Add(new RawPrimitive(
                        primitives.Count,
                        new RectD(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)));
                    break;
                }

                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    CollectPrimitives(drawPicture.Picture, matrix, primitives);
                    break;
            }
        }
    }
}
