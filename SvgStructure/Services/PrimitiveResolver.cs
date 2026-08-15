using SvgStructure.Models;
using Svg.Skia;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

/// <summary>
/// Pipeline step 2. Resolves real SVG content primitives against the logical map produced by
/// <see cref="PartMeasureResolver"/>.
///
/// Primitives confidently owned by one staff-measure receive Pn-Mm coordinates.
/// Everything else is kept available to later recognition steps at measure scope (Mm only).
/// Staff lines are temporary classification anchors and giant garbage shapes are discarded.
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

        // Staff lines participate only as virtual/physical anchors inside the classifier.
        // They are not recognition output.
        var staffLineIds = _staffLineDetector.Detect(raw, regions);
        var content = raw.Where(x => !staffLineIds.Contains(x.Id)).ToArray();

        // Giant page-spanning shapes are renderer/SVG infrastructure noise. Remove them entirely.
        var cleanup = _garbageCleaner.Clean(content, regions);
        var claims = _classifier.Classify(cleanup.Primitives, regions, structure.Map.PageBounds);

        var resolved = cleanup.Primitives
            .Select(primitive => ResolvePrimitive(primitive, structure, claims))
            .ToArray();

        return new PrimitiveResolution(structure, resolved);
    }

    private static ResolvedPrimitive ResolvePrimitive(
        RawPrimitive primitive,
        PartMeasureResolution structure,
        IReadOnlyDictionary<int, HashSet<StaffMeasureKey>> claims)
    {
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

        // Deliberately broad fallback: pedals, dynamics, hairpins, cross-staff lines and even
        // currently-uninteresting page text must stay visible to subsequent steps. Attach every
        // unresolved content primitive to the physically nearest measure, without guessing a part.
        var measureNumber = ResolveNearestMeasure(primitive.Bounds, structure.Map);
        if (measureNumber is not null)
        {
            return new ResolvedPrimitive(
                primitive.Id,
                primitive.Bounds,
                PrimitiveLogicalScope.Measure,
                null,
                measureNumber);
        }

        // This should only happen for a malformed/empty logical map.
        return new ResolvedPrimitive(
            primitive.Id,
            primitive.Bounds,
            PrimitiveLogicalScope.PhysicalOnly,
            null,
            null);
    }

    private static int? ResolveNearestMeasure(RectD bounds, PartMeasureMap map)
    {
        if (map.Blocks.Count == 0)
            return null;

        var measureBounds = map.Blocks
            .GroupBy(x => x.MeasureNumber)
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

        var centerX = bounds.CenterX;
        var horizontalCandidates = measureBounds
            .Where(x => centerX >= x.Bounds.Left && centerX <= x.Bounds.Right)
            .ToArray();

        var candidates = horizontalCandidates.Length > 0
            ? horizontalCandidates
            : measureBounds;

        return candidates
            .OrderBy(x => RectangleDistance(bounds, x.Bounds))
            .ThenBy(x => Math.Abs(bounds.CenterX - x.Bounds.CenterX))
            .Select(x => (int?)x.MeasureNumber)
            .FirstOrDefault();
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
                    var mappedBounds = matrix.MapRect(drawPath.Path.Bounds);
                    primitives.Add(new RawPrimitive(
                        primitives.Count,
                        new RectD(
                            mappedBounds.Left,
                            mappedBounds.Top,
                            mappedBounds.Right,
                            mappedBounds.Bottom)));
                    break;
                }

                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    CollectPrimitives(drawPicture.Picture, matrix, primitives);
                    break;
            }
        }
    }
}
