using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Svg.Skia;

namespace SvgStructure.Services;

public sealed record SvgSourceModelDumpResult(
    int ElementCount,
    int UseCount,
    string TreePath,
    string UsesPath);

/// <summary>
/// Diagnostic probe for Svg.Skia's pre-render SVG object model. This deliberately walks
/// SKSvg.SourceDocument rather than the flattened retained SKPicture model so we can see whether
/// semantic elements such as &lt;use&gt; survive parsing and what instance metadata is available.
/// Reflection is used for element-specific properties to keep the diagnostic resilient across
/// Svg.Skia/Svg package versions.
/// </summary>
public sealed class SvgSourceModelDumper
{
    public SvgSourceModelDumpResult Dump(string svgPath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var treePath = Path.Combine(outputDirectory, "source-tree.txt");
        var usesPath = Path.Combine(outputDirectory, "source-uses.json");

        using var svg = SKSvg.CreateFromFile(svgPath);
        var sourceDocument = typeof(SKSvg)
            .GetProperty("SourceDocument", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(svg);

        if (sourceDocument is null)
        {
            File.WriteAllText(treePath, "SKSvg.SourceDocument is not available in this Svg.Skia build.\n");
            File.WriteAllText(usesPath, "[]");
            return new SvgSourceModelDumpResult(0, 0, treePath, usesPath);
        }

        var tree = new StringBuilder();
        var uses = new List<object>();
        var elementCount = 0;
        var useCount = 0;

        Walk(sourceDocument, "0", 0);

        File.WriteAllText(treePath, tree.ToString());
        File.WriteAllText(
            usesPath,
            JsonSerializer.Serialize(uses, new JsonSerializerOptions { WriteIndented = true }));

        return new SvgSourceModelDumpResult(elementCount, useCount, treePath, usesPath);

        void Walk(object node, string path, int depth)
        {
            elementCount++;
            var type = node.GetType();
            var typeName = type.Name;
            var elementName = Read(node, "ElementName");
            var id = Read(node, "ID") ?? Read(node, "Id");
            var reference = Read(node, "ReferencedElement") ?? Read(node, "Href") ?? Read(node, "Reference");
            var transforms = Read(node, "Transforms") ?? Read(node, "Transform");
            var x = Read(node, "X");
            var y = Read(node, "Y");
            var width = Read(node, "Width");
            var height = Read(node, "Height");
            var isUse = string.Equals(typeName, "SvgUse", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(elementName, "use", StringComparison.OrdinalIgnoreCase);

            tree.Append(' ', depth * 2);
            tree.Append(path).Append("  ").Append(typeName);
            if (!string.IsNullOrWhiteSpace(elementName)) tree.Append(" <").Append(elementName).Append('>');
            if (!string.IsNullOrWhiteSpace(id)) tree.Append(" id=").Append(id);
            if (!string.IsNullOrWhiteSpace(reference)) tree.Append(" ref=").Append(reference);
            if (!string.IsNullOrWhiteSpace(transforms)) tree.Append(" transform=").Append(transforms);
            if (!string.IsNullOrWhiteSpace(x) || !string.IsNullOrWhiteSpace(y))
                tree.Append(" xy=").Append(x ?? "?").Append(',').Append(y ?? "?");
            tree.AppendLine();

            if (isUse)
            {
                useCount++;
                uses.Add(new
                {
                    path,
                    type = typeName,
                    elementName,
                    id,
                    reference,
                    transforms,
                    x,
                    y,
                    width,
                    height,
                    properties = InterestingProperties(node)
                });
            }

            var children = Children(node).ToArray();
            for (var i = 0; i < children.Length; i++)
                Walk(children[i], path + "/" + i.ToString(CultureInfo.InvariantCulture), depth + 1);
        }
    }

    private static IEnumerable<object> Children(object node)
    {
        var value = node.GetType().GetProperty("Children", BindingFlags.Instance | BindingFlags.Public)?.GetValue(node);
        if (value is not IEnumerable enumerable || value is string)
            yield break;

        foreach (var child in enumerable)
        {
            if (child is not null)
                yield return child;
        }
    }

    private static string? Read(object node, string propertyName)
    {
        try
        {
            var property = node.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            var value = property?.GetValue(node);
            return Format(value);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string?> InterestingProperties(object node)
    {
        var names = new[]
        {
            "ID", "ElementName", "ReferencedElement", "Href", "Reference", "Transforms",
            "X", "Y", "Width", "Height", "CustomAttributes"
        };
        return names.ToDictionary(x => x, x => Read(node, x));
    }

    private static string? Format(object? value)
    {
        if (value is null)
            return null;
        if (value is string text)
            return text;
        if (value is Uri uri)
            return uri.OriginalString;
        if (value is IEnumerable enumerable)
        {
            var items = new List<string>();
            foreach (var item in enumerable)
            {
                if (item is null) continue;
                items.Add(item.ToString() ?? string.Empty);
                if (items.Count >= 16) break;
            }
            return items.Count == 0 ? value.ToString() : string.Join("; ", items);
        }
        return value.ToString();
    }
}
