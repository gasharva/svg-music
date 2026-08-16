using System.Collections;
using System.Globalization;
using System.Reflection;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Bridges Svg.Skia's two representations of the document:
/// - SourceDocument keeps semantic SVG elements such as SvgUse;
/// - Model contains the flattened draw commands used by PrimitiveResolver.
///
/// A flattened draw path normally keeps SourceElementAddress of the referenced definition, not of
/// the concrete &lt;use&gt; instance. We therefore resolve every use's href to the definition path in
/// SourceDocument and then choose the nearest concrete use instance for contours coming from that
/// definition subtree.
/// </summary>
public sealed class SvgUseInstanceMapper
{
    public IReadOnlyList<RawPrimitive> Map(SKSvg svg, IReadOnlyList<RawPrimitive> primitives)
    {
        var sourceDocument = typeof(SKSvg)
            .GetProperty("SourceDocument", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(svg);
        if (sourceDocument is null || primitives.Count == 0)
            return primitives;

        var nodes = new List<SourceNode>();
        Walk(sourceDocument, "0", nodes);

        var idToPath = nodes
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Path, StringComparer.Ordinal);

        var uses = nodes
            .Where(x => x.IsUse && !string.IsNullOrWhiteSpace(x.Reference))
            .Select(x => BuildUse(x, idToPath))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        if (uses.Length == 0)
            return primitives;

        var byAddress = new Dictionary<string, SourceUseInstance[]>(StringComparer.Ordinal);
        foreach (var address in primitives
                     .Select(x => NormalizeAddress(x.Source.ElementAddress))
                     .Where(x => x is not null)
                     .Select(x => x!)
                     .Distinct(StringComparer.Ordinal))
        {
            var candidates = uses
                .Where(x => AddressBelongsToTarget(address, x.TargetPath))
                .ToArray();
            if (candidates.Length > 0)
                byAddress[address] = candidates;
        }

        return primitives.Select(primitive =>
        {
            var address = NormalizeAddress(primitive.Source.ElementAddress);
            if (address is null || !byAddress.TryGetValue(address, out var candidates))
                return primitive;

            var use = candidates
                .OrderBy(x => DistanceSquared(primitive.Bounds.CenterX, primitive.Bounds.CenterY, x.X, x.Y))
                .ThenBy(x => x.Path, StringComparer.Ordinal)
                .First();

            var source = primitive.Source with
            {
                GroupAnchor = "use:" + use.Path,
                IsExplicitUse = true,
                ReferenceAnchor = use.Reference,
                InstanceX = use.X,
                InstanceY = use.Y
            };
            return primitive with { Source = source };
        }).ToArray();
    }

    private static SourceUseInstance? BuildUse(
        SourceNode node,
        IReadOnlyDictionary<string, string> idToPath)
    {
        var reference = node.Reference!;
        if (!reference.StartsWith('#'))
            return null;

        var id = reference[1..];
        if (id.Length == 0 || !idToPath.TryGetValue(id, out var targetPath))
            return null;

        return new SourceUseInstance(
            node.Path,
            reference,
            targetPath,
            node.X ?? 0,
            node.Y ?? 0);
    }

    private static void Walk(object node, string path, ICollection<SourceNode> result)
    {
        var typeName = node.GetType().Name;
        var elementName = ReadText(node, "ElementName");
        var isUse = string.Equals(typeName, "SvgUse", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(elementName, "use", StringComparison.OrdinalIgnoreCase);

        result.Add(new SourceNode(
            path,
            ReadText(node, "ID") ?? ReadText(node, "Id"),
            ReadText(node, "ReferencedElement") ?? ReadText(node, "Href") ?? ReadText(node, "Reference"),
            ReadNumber(node, "X"),
            ReadNumber(node, "Y"),
            isUse));

        var children = Children(node).ToArray();
        for (var i = 0; i < children.Length; i++)
            Walk(children[i], path + "/" + i.ToString(CultureInfo.InvariantCulture), result);
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

    private static double? ReadNumber(object node, string propertyName)
    {
        try
        {
            var value = node.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(node);
            if (value is null)
                return null;
            if (value is IConvertible convertible)
                return convertible.ToDouble(CultureInfo.InvariantCulture);

            var text = value.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var numeric = new string(text
                .TakeWhile(c => char.IsDigit(c) || c is '-' or '+' or '.' or ',' or 'e' or 'E')
                .ToArray())
                .Replace(',', '.');
            return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;
        return address.StartsWith("xml:", StringComparison.Ordinal) ? address[4..] : address;
    }

    private static bool AddressBelongsToTarget(string address, string targetPath) =>
        string.Equals(address, targetPath, StringComparison.Ordinal) ||
        address.StartsWith(targetPath + "/", StringComparison.Ordinal);

    private static double DistanceSquared(double ax, double ay, double bx, double by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return dx * dx + dy * dy;
    }

    private sealed record SourceNode(
        string Path,
        string? Id,
        string? Reference,
        double? X,
        double? Y,
        bool IsUse);

    private sealed record SourceUseInstance(
        string Path,
        string Reference,
        string TargetPath,
        double X,
        double Y);
}
