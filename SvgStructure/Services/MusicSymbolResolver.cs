using System.Reflection;
using Svg.Skia;
using SvgStructure.Models;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

/// <summary>
/// Bridge between low-level flattened primitives and semantic recognizers.
/// PrimitiveResolver geometry is used only as a spatial grouping scaffold. Smooth recognition
/// geometry comes from Svg.Skia's retained scene graph, which already contains the complete SVG
/// transform chain and keeps the original Bezier PathData on its source elements.
/// </summary>
public sealed class MusicSymbolResolver
{
    public MusicSymbolResolution Resolve(PrimitiveResolution primitives)
    {
        using var svg = SKSvg.CreateFromFile(primitives.Structure.SvgPath);
        _ = svg.RetainedSceneGraph; // force compilation once; subsequent lookups are cached by Svg.Skia

        var candidates = new List<MusicSymbolCandidate>();
        var nextId = 0;

        var usable = primitives.Primitives
            .Where(x => x.Scope is PrimitiveLogicalScope.PartMeasure or PrimitiveLogicalScope.Measure)
            .Where(x => x.MeasureNumber is not null)
            .ToArray();

        foreach (var bucket in usable
                     .GroupBy(x => new BucketKey(x.Scope, x.PartNumber, x.MeasureNumber!.Value))
                     .OrderBy(x => x.Key.MeasureNumber)
                     .ThenBy(x => x.Key.PartNumber ?? int.MaxValue))
        {
            var remaining = bucket
                .OrderByDescending(x => Area(x.PhysicalBounds))
                .ThenBy(x => x.PhysicalBounds.Left)
                .ThenBy(x => x.Id)
                .ToList();

            while (remaining.Count > 0)
            {
                var anchor = remaining[0];
                remaining.RemoveAt(0);

                var members = new List<ResolvedPrimitive> { anchor };
                for (var i = remaining.Count - 1; i >= 0; i--)
                {
                    if (!HasPositiveAreaOverlap(anchor.PhysicalBounds, remaining[i].PhysicalBounds))
                        continue;
                    members.Add(remaining[i]);
                    remaining.RemoveAt(i);
                }

                var bounds = Union(members.Select(x => x.PhysicalBounds));
                var sources = members
                    .Select(x => x.Source)
                    .DistinctBy(SourceIdentity, StringComparer.Ordinal)
                    .ToArray();

                var smoothPaths = members
                    .SelectMany(member => ResolveSmoothPaths(svg, member))
                    .DistinctBy(x => $"{x.SourceAddress}\n{x.PathData}\n{x.Transform}", StringComparer.Ordinal)
                    .ToArray();

                candidates.Add(new MusicSymbolCandidate(
                    nextId++,
                    bucket.Key.Scope,
                    bucket.Key.PartNumber,
                    bucket.Key.MeasureNumber,
                    bounds,
                    members.Select(x => x.Id).OrderBy(x => x).ToArray(),
                    members.Select(x => x.PhysicalBounds).ToArray(),
                    sources,
                    smoothPaths));
            }
        }

        var ordered = candidates
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber ?? int.MaxValue)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ThenBy(x => x.PhysicalBounds.Top)
            .Select((x, i) => x with { Id = i })
            .ToArray();

        return new MusicSymbolResolution(primitives, ordered);
    }

    private static IEnumerable<SmoothSvgPath> ResolveSmoothPaths(SKSvg svg, ResolvedPrimitive primitive)
    {
        var address = NormalizeAddress(primitive.Source.ElementAddress);
        if (address is null || !svg.TryGetRetainedSceneNodes(address, out var nodes) || nodes.Count == 0)
            yield break;

        // One source element can be rendered many times through <use>. Svg.Skia retains a separate
        // scene node for each rendered occurrence; choose the occurrence whose transformed bounds
        // are physically closest to the PrimitiveResolver artifact that led us here.
        var root = nodes
            .OrderBy(node => RectangleDistance(primitive.PhysicalBounds, ToRectD(node.TransformedBounds)))
            .ThenBy(node => CenterDistanceSquared(primitive.PhysicalBounds, ToRectD(node.TransformedBounds)))
            .First();

        foreach (var node in DescendantsAndSelf(root))
        {
            var pathData = ReadPathData(node.Element);
            if (string.IsNullOrWhiteSpace(pathData))
                continue;

            yield return new SmoothSvgPath(
                node.ElementAddressKey ?? address,
                pathData!,
                Matrix(node.TotalTransform));
        }
    }

    private static IEnumerable<SvgSceneNode> DescendantsAndSelf(SvgSceneNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var nested in DescendantsAndSelf(child))
                yield return nested;
    }

    private static string? ReadPathData(object? element)
    {
        if (element is null)
            return null;
        var type = element.GetType();
        if (!string.Equals(type.Name, "SvgPath", StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var propertyName in new[] { "PathData", "D" })
        {
            try
            {
                var value = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(element);
                var text = value?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            catch { }
        }
        return null;
    }

    private static string Matrix(Shim.SKMatrix matrix) =>
        $"matrix({F(matrix.ScaleX)} {F(matrix.SkewY)} {F(matrix.SkewX)} {F(matrix.ScaleY)} {F(matrix.TransX)} {F(matrix.TransY)})";

    private static string F(float value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static RectD ToRectD(Shim.SKRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static double RectangleDistance(RectD a, RectD b)
    {
        var dx = a.Right < b.Left ? b.Left - a.Right : b.Right < a.Left ? a.Left - b.Right : 0;
        var dy = a.Bottom < b.Top ? b.Top - a.Bottom : b.Bottom < a.Top ? a.Top - b.Bottom : 0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double CenterDistanceSquared(RectD a, RectD b)
    {
        var dx = a.CenterX - b.CenterX;
        var dy = a.CenterY - b.CenterY;
        return dx * dx + dy * dy;
    }

    private static string SourceIdentity(PrimitiveSourceRef source) =>
        $"{source.ElementAddress}|{source.GroupAnchor}|{source.ReferenceAnchor}|{source.InstanceX}|{source.InstanceY}";

    private static string? NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        return address.StartsWith("xml:", StringComparison.Ordinal) ? address[4..] : address;
    }

    private static double Area(RectD rect) => rect.Width * rect.Height;

    private static bool HasPositiveAreaOverlap(RectD a, RectD b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return width > 1e-6 && height > 1e-6;
    }

    private static RectD Union(IEnumerable<RectD> rects)
    {
        var values = rects.ToArray();
        return new RectD(
            values.Min(x => x.Left),
            values.Min(x => x.Top),
            values.Max(x => x.Right),
            values.Max(x => x.Bottom));
    }

    private readonly record struct BucketKey(
        PrimitiveLogicalScope Scope,
        int? PartNumber,
        int MeasureNumber);
}
