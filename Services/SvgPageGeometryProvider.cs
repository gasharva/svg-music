using System.Globalization;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Resolves every reusable symbol use and every standalone path into the same page-coordinate
/// geometry stream. Structural recognition (stems, beams, arcs, barlines, ledger lines) should
/// consume this stream instead of branching on how an exporter encoded the shape in SVG.
/// </summary>
public sealed class SvgPageGeometryProvider
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private readonly SvgPathGeometry _geometry = new();

    public List<SvgPageGeometry> Read(XDocument document)
    {
        var result = new List<SvgPageGeometry>();
        var symbols = _geometry.ReadSymbols(document);
        var useIndex = 0;

        foreach (var use in document.Descendants(Svg + "use"))
        {
            if (use.Ancestors(Svg + "symbol").Any() || use.Ancestors(Svg + "defs").Any())
                continue;

            var symbolId = ((string?)use.Attribute(XLink + "href") ?? (string?)use.Attribute("href") ?? "")
                .TrimStart('#');
            if (string.IsNullOrWhiteSpace(symbolId) || !symbols.TryGetValue(symbolId, out var symbol))
                continue;

            var x = Parse((string?)use.Attribute("x"));
            var y = Parse((string?)use.Attribute("y"));
            var transform = SvgAffine.Translate(x, y).Then(SvgPathGeometry.ReadTransformChain(use));
            var contours = Apply(symbol.Contours, transform);
            Add(result, $"use:{useIndex++:D6}", symbolId, "use", contours);
        }

        foreach (var path in _geometry.ReadDirectPaths(document))
            Add(result, path.SymbolId, null, "path", path.Geometry.Contours);

        return result;
    }

    private static void Add(
        ICollection<SvgPageGeometry> target,
        string instanceId,
        string? sourceSymbolId,
        string sourceKind,
        IReadOnlyList<IReadOnlyList<PointD>> contours)
    {
        var all = contours.SelectMany(x => x).ToArray();
        if (all.Length == 0) return;

        var geometry = new SymbolGeometry(instanceId, contours);
        target.Add(new SvgPageGeometry(
            instanceId,
            sourceSymbolId,
            sourceKind,
            geometry,
            (all.Min(x => x.X) + all.Max(x => x.X)) / 2,
            (all.Min(x => x.Y) + all.Max(x => x.Y)) / 2));
    }

    private static IReadOnlyList<IReadOnlyList<PointD>> Apply(
        IEnumerable<IReadOnlyList<PointD>> contours,
        SvgAffine transform) =>
        contours.Select(c => (IReadOnlyList<PointD>)c.Select(transform.Apply).ToArray()).ToArray();

    private static double Parse(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
}
