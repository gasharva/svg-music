using System.Collections;
using System.Globalization;
using System.Reflection;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Step 3-ish bridge between low-level flattened primitives and semantic recognizers.
/// PrimitiveResolver geometry is used only as a spatial grouping scaffold. The candidate's actual
/// recognition geometry is recovered from Svg.Skia.SourceDocument so Bezier path data stays smooth.
/// </summary>
public sealed class MusicSymbolResolver
{
    public MusicSymbolResolution Resolve(PrimitiveResolution primitives)
    {
        using var svg = SKSvg.CreateFromFile(primitives.Structure.SvgPath);
        var sourceIndex = SourceIndex.Build(svg);

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
                // Largest remaining primitive becomes the anchor. Only strict positive-area overlap
                // with this anchor counts; mere touching is intentionally not grouped yet.
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
                    .DistinctBy(x => SourceIdentity(x), StringComparer.Ordinal)
                    .ToArray();
                var smoothPaths = sources
                    .SelectMany(sourceIndex.ResolveSmoothPaths)
                    .DistinctBy(x => $"{x.SourceAddress}\n{x.PathData}\n{x.Transform}", StringComparer.Ordinal)
                    .ToArray();

                candidates.Add(new MusicSymbolCandidate(
                    nextId++,
                    bucket.Key.Scope,
                    bucket.Key.PartNumber,
                    bucket.Key.MeasureNumber,
                    bounds,
                    members.Select(x => x.Id).OrderBy(x => x).ToArray(),
                    sources,
                    smoothPaths));
            }
        }

        // Stable reading order for consumers and diagnostics.
        var ordered = candidates
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber ?? int.MaxValue)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ThenBy(x => x.PhysicalBounds.Top)
            .Select((x, i) => x with { Id = i })
            .ToArray();

        return new MusicSymbolResolution(primitives, ordered);
    }

    private static string SourceIdentity(PrimitiveSourceRef source) =>
        $"{source.ElementAddress}|{source.GroupAnchor}|{source.ReferenceAnchor}|{source.InstanceX}|{source.InstanceY}";

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

    private sealed class SourceIndex
    {
        private readonly Dictionary<string, SourceNode> _byAddress;

        private SourceIndex(Dictionary<string, SourceNode> byAddress)
        {
            _byAddress = byAddress;
        }

        public static SourceIndex Build(SKSvg svg)
        {
            var document = typeof(SKSvg)
                .GetProperty("SourceDocument", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(svg);
            var byAddress = new Dictionary<string, SourceNode>(StringComparer.Ordinal);
            if (document is null)
                return new SourceIndex(byAddress);

            WalkDocument(document, byAddress);
            return new SourceIndex(byAddress);
        }

        public IEnumerable<SmoothSvgPath> ResolveSmoothPaths(PrimitiveSourceRef source)
        {
            var address = NormalizeAddress(source.ElementAddress);
            if (address is null || !_byAddress.TryGetValue(address, out var node))
                yield break;

            foreach (var pathNode in DescendantPaths(node))
            {
                var pathData = ReadText(pathNode.Value, "PathData") ?? ReadText(pathNode.Value, "D");
                if (string.IsNullOrWhiteSpace(pathData))
                    continue;

                var transforms = new List<string>();
                if (source.IsExplicitUse && source.InstanceX is not null && source.InstanceY is not null)
                    transforms.Add($"translate({F(source.InstanceX.Value)} {F(source.InstanceY.Value)})");
                if (!string.IsNullOrWhiteSpace(pathNode.AccumulatedTransform))
                    transforms.Add(pathNode.AccumulatedTransform!);

                yield return new SmoothSvgPath(
                    pathNode.Address,
                    pathData,
                    transforms.Count == 0 ? null : string.Join(" ", transforms));
            }
        }

        private static IEnumerable<SourceNode> DescendantPaths(SourceNode node)
        {
            if (node.TypeName.Equals("SvgPath", StringComparison.OrdinalIgnoreCase))
                yield return node;
            foreach (var child in node.Children)
                foreach (var nested in DescendantPaths(child))
                    yield return nested;
        }

        private static void WalkDocument(object document, IDictionary<string, SourceNode> index)
        {
            var children = Children(document).ToArray();
            for (var i = 0; i < children.Length; i++)
                Walk(children[i], i.ToString(CultureInfo.InvariantCulture), null, index);
        }

        private static SourceNode Walk(
            object value,
            string address,
            string? inheritedTransform,
            IDictionary<string, SourceNode> index)
        {
            var ownTransform = ReadText(value, "Transforms") ?? ReadText(value, "Transform");
            var accumulated = JoinTransforms(inheritedTransform, ownTransform);
            var node = new SourceNode(address, value, value.GetType().Name, accumulated);
            index[address] = node;

            var children = Children(value).ToArray();
            for (var i = 0; i < children.Length; i++)
            {
                var childAddress = address + "/" + i.ToString(CultureInfo.InvariantCulture);
                node.Children.Add(Walk(children[i], childAddress, accumulated, index));
            }
            return node;
        }

        private static string? JoinTransforms(string? outer, string? inner)
        {
            if (string.IsNullOrWhiteSpace(outer)) return string.IsNullOrWhiteSpace(inner) ? null : inner;
            if (string.IsNullOrWhiteSpace(inner)) return outer;
            return outer + " " + inner;
        }

        private static IEnumerable<object> Children(object node)
        {
            var value = node.GetType().GetProperty("Children", BindingFlags.Instance | BindingFlags.Public)?.GetValue(node);
            if (value is not IEnumerable enumerable || value is string)
                yield break;
            foreach (var child in enumerable)
                if (child is not null)
                    yield return child;
        }

        private static string? ReadText(object node, string propertyName)
        {
            try
            {
                var value = node.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(node);
                return value switch
                {
                    null => null,
                    Uri uri => uri.OriginalString,
                    _ => value.ToString()
                };
            }
            catch
            {
                return null;
            }
        }

        private static string? NormalizeAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            return address.StartsWith("xml:", StringComparison.Ordinal) ? address[4..] : address;
        }

        private static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private sealed class SourceNode(
            string address,
            object value,
            string typeName,
            string? accumulatedTransform)
        {
            public string Address { get; } = address;
            public object Value { get; } = value;
            public string TypeName { get; } = typeName;
            public string? AccumulatedTransform { get; } = accumulatedTransform;
            public List<SourceNode> Children { get; } = new();
        }
    }
}
