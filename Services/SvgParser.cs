using System.Globalization;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class SvgParser
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private readonly SvgPathGeometry _geometry = new();

    public XDocument Load(string path) => XDocument.Load(path, LoadOptions.PreserveWhitespace);

    /// <summary>
    /// Returns one unified stream of glyph instances. Reused SVG symbols keep their
    /// symbol id; standalone paths receive stable synthetic ids path:000000, ... .
    /// </summary>
    public List<SvgUse> ReadUses(XDocument document)
    {
        var result = document.Descendants(Svg + "use")
            .Select(x =>
            {
                var id = ((string?)x.Attribute(XLink + "href") ?? (string?)x.Attribute("href") ?? "").TrimStart('#');
                var position = SvgPathGeometry.ReadTransformChain(x).Apply(
                    Parse((string?)x.Attribute("x")),
                    Parse((string?)x.Attribute("y")));
                return new SvgUse(id, position.X, position.Y, "use");
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.SymbolId))
            .ToList();

        result.AddRange(_geometry.ReadDirectPaths(document)
            .Select(x => new SvgUse(x.SymbolId, x.X, x.Y, "path")));
        return result;
    }

    public Dictionary<string, int> CountSymbols(XDocument document) => ReadUses(document)
        .GroupBy(x => $"{x.SourceKind}:{x.SymbolId}")
        .OrderByDescending(x => x.Count())
        .ToDictionary(x => x.Key, x => x.Count());

    public List<Staff> DetectStaves(XDocument document, double tolerance = 0.25)
    {
        var horizontal = new List<(double X1, double X2, double Y)>();

        // All standalone paths are already expanded to world coordinates, including
        // inherited group transforms. This works for path-only and mixed SVG files.
        foreach (var path in _geometry.ReadDirectPaths(document))
        {
            foreach (var contour in path.Geometry.Contours)
            {
                for (var i = 1; i < contour.Count; i++)
                {
                    var p1 = contour[i - 1];
                    var p2 = contour[i];
                    if (Math.Abs(p1.Y - p2.Y) <= tolerance && Math.Abs(p2.X - p1.X) > 100)
                        horizontal.Add((Math.Min(p1.X, p2.X), Math.Max(p1.X, p2.X), (p1.Y + p2.Y) / 2));
                }
            }
        }

        // Some exporters encode staff lines as <line> rather than <path>.
        foreach (var line in document.Descendants(Svg + "line"))
        {
            if (line.Ancestors(Svg + "defs").Any() || line.Ancestors(Svg + "symbol").Any()) continue;
            var transform = SvgPathGeometry.ReadTransformChain(line);
            var p1 = transform.Apply(Parse((string?)line.Attribute("x1")), Parse((string?)line.Attribute("y1")));
            var p2 = transform.Apply(Parse((string?)line.Attribute("x2")), Parse((string?)line.Attribute("y2")));
            if (Math.Abs(p1.Y - p2.Y) <= tolerance && Math.Abs(p2.X - p1.X) > 100)
                horizontal.Add((Math.Min(p1.X, p2.X), Math.Max(p1.X, p2.X), (p1.Y + p2.Y) / 2));
        }

        var candidates = horizontal
            .GroupBy(x => Math.Round(x.Y / tolerance) * tolerance)
            .Select(g => (Y: g.Average(x => x.Y), Left: g.Min(x => x.X1), Right: g.Max(x => x.X2)))
            .OrderBy(x => x.Y)
            .ToList();

        var staves = new List<Staff>();
        for (var i = 0; i <= candidates.Count - 5; i++)
        {
            var block = candidates.Skip(i).Take(5).ToArray();
            var spaces = block.Zip(block.Skip(1), (a, b) => b.Y - a.Y).ToArray();
            var mean = spaces.Average();
            if (mean < 2 || mean > 20) continue;
            if (spaces.Any(s => Math.Abs(s - mean) > Math.Max(tolerance * 2, mean * 0.08))) continue;
            if (block.Min(x => x.Right) - block.Max(x => x.Left) < 100) continue;

            staves.Add(new Staff(staves.Count,
                block.Max(x => x.Left), block.Min(x => x.Right), block.Select(x => x.Y).ToArray()));
            i += 4;
        }

        return staves;
    }

    private static double Parse(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
}
