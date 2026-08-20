using System.Xml.Linq;

namespace MusicXml;

/// <summary>
/// Diagnostic-only XML comparison used by the round-trip CLI. Production/domain code should not depend on this.
/// </summary>
internal static class SemanticXmlDiff
{
    public static IReadOnlyList<string> Compare(string beforePath, string afterPath, int maxDifferences = 500)
    {
        var before = XDocument.Load(beforePath).Root
            ?? throw new InvalidDataException($"No root element in {beforePath}.");
        var after = XDocument.Load(afterPath).Root
            ?? throw new InvalidDataException($"No root element in {afterPath}.");

        var differences = new List<string>();
        CompareElement(before, after, "/" + before.Name.LocalName, differences, maxDifferences);
        return differences;
    }

    private static void CompareElement(
        XElement before,
        XElement after,
        string path,
        List<string> differences,
        int maxDifferences)
    {
        if (differences.Count >= maxDifferences)
            return;

        if (before.Name.LocalName != after.Name.LocalName)
        {
            Add(differences, maxDifferences,
                $"{path}: ELEMENT changed '{before.Name.LocalName}' -> '{after.Name.LocalName}'");
            return;
        }

        var beforeAttributes = before.Attributes()
            .Where(x => !x.IsNamespaceDeclaration)
            .ToDictionary(x => x.Name.ToString(), x => x.Value, StringComparer.Ordinal);
        var afterAttributes = after.Attributes()
            .Where(x => !x.IsNamespaceDeclaration)
            .ToDictionary(x => x.Name.ToString(), x => x.Value, StringComparer.Ordinal);

        foreach (var key in beforeAttributes.Keys.Except(afterAttributes.Keys).OrderBy(x => x, StringComparer.Ordinal))
            Add(differences, maxDifferences, $"{path}: LOST attribute {key}='{beforeAttributes[key]}'");

        foreach (var key in afterAttributes.Keys.Except(beforeAttributes.Keys).OrderBy(x => x, StringComparer.Ordinal))
            Add(differences, maxDifferences, $"{path}: ADDED attribute {key}='{afterAttributes[key]}'");

        foreach (var key in beforeAttributes.Keys.Intersect(afterAttributes.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!string.Equals(beforeAttributes[key], afterAttributes[key], StringComparison.Ordinal))
                Add(differences, maxDifferences,
                    $"{path}: CHANGED attribute {key}: '{beforeAttributes[key]}' -> '{afterAttributes[key]}'");
        }

        var beforeText = CleanText(before);
        var afterText = CleanText(after);
        if (!string.Equals(beforeText, afterText, StringComparison.Ordinal))
            Add(differences, maxDifferences, $"{path}: TEXT changed '{beforeText}' -> '{afterText}'");

        var beforeChildren = before.Elements().ToArray();
        var afterChildren = after.Elements().ToArray();
        if (beforeChildren.Length != afterChildren.Length)
            Add(differences, maxDifferences,
                $"{path}: CHILD COUNT changed {beforeChildren.Length} -> {afterChildren.Length}");

        var common = Math.Min(beforeChildren.Length, afterChildren.Length);
        for (var i = 0; i < common && differences.Count < maxDifferences; i++)
        {
            var childPath = path + "/" + Describe(beforeChildren[i], i);
            CompareElement(beforeChildren[i], afterChildren[i], childPath, differences, maxDifferences);
        }

        for (var i = common; i < beforeChildren.Length; i++)
            Add(differences, maxDifferences, $"{path}: LOST child {Describe(beforeChildren[i], i)}");

        for (var i = common; i < afterChildren.Length; i++)
            Add(differences, maxDifferences, $"{path}: ADDED child {Describe(afterChildren[i], i)}");
    }

    private static string Describe(XElement element, int index)
    {
        var tag = element.Name.LocalName;
        if (tag == "measure" && element.Attribute("number") is { } measureNumber)
            return $"measure[@number='{measureNumber.Value}']";

        var hints = new[] { "number", "id", "type", "staff", "voice" }
            .Select(name => (Name: name, Attribute: element.Attribute(name)))
            .Where(x => x.Attribute is not null)
            .Select(x => $"{x.Name}='{x.Attribute!.Value}'")
            .ToArray();

        return hints.Length == 0
            ? $"{tag}[{index}]"
            : $"{tag}[{index}][{string.Join(", ", hints)}]";
    }

    private static string CleanText(XElement element) =>
        string.Join(" ", element.Nodes().OfType<XText>().Select(x => x.Value)
            .SelectMany(x => x.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

    private static void Add(List<string> differences, int maxDifferences, string message)
    {
        if (differences.Count < maxDifferences)
            differences.Add(message);
    }
}
