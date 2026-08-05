using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class SvgParser
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly Regex Number = new(@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);
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

    /// <summary>
    /// Reads layout geometry which should not have to pass through the musical glyph classifier.
    /// This includes SVG line elements, two-point polylines and narrow rectangles. All coordinates
    /// are returned in world space after applying inherited transforms.
    /// </summary>
    public List<SvgLineSegment> ReadLineSegments(XDocument document)
    {
        var result = new List<SvgLineSegment>();

        foreach (var line in document.Descendants(Svg + "line"))
        {
            if (IsDefinitionElement(line)) continue;
            var transform = SvgPathGeometry.ReadTransformChain(line);
            var p1 = transform.Apply(Parse((string?)line.Attribute("x1")), Parse((string?)line.Attribute("y1")));
            var p2 = transform.Apply(Parse((string?)line.Attribute("x2")), Parse((string?)line.Attribute("y2")));
            result.Add(new SvgLineSegment(p1.X, p1.Y, p2.X, p2.Y, "line", (string?)line.Attribute("class")));
        }

        foreach (var polyline in document.Descendants(Svg + "polyline"))
        {
            if (IsDefinitionElement(polyline)) continue;
            var values = ParsePointValues(polyline);
            if (values.Length != 4) continue;

            var transform = SvgPathGeometry.ReadTransformChain(polyline);
            var p1 = transform.Apply(values[0], values[1]);
            var p2 = transform.Apply(values[2], values[3]);
            result.Add(new SvgLineSegment(p1.X, p1.Y, p2.X, p2.Y, "polyline",
                (string?)polyline.Attribute("class")));
        }

        foreach (var rect in document.Descendants(Svg + "rect"))
        {
            if (IsDefinitionElement(rect)) continue;
            var x = Parse((string?)rect.Attribute("x"));
            var y = Parse((string?)rect.Attribute("y"));
            var width = Parse((string?)rect.Attribute("width"));
            var height = Parse((string?)rect.Attribute("height"));
            var cssClass = (string?)rect.Attribute("class");

            if (width <= 0 || height <= 0) continue;
            if (!ContainsBarline(cssClass) && width > height * .25) continue;

            var transform = SvgPathGeometry.ReadTransformChain(rect);
            var top = transform.Apply(x + width / 2, y);
            var bottom = transform.Apply(x + width / 2, y + height);
            result.Add(new SvgLineSegment(top.X, top.Y, bottom.X, bottom.Y, "rect", cssClass));
        }

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
                    AddHorizontal(horizontal, contour[i - 1], contour[i], tolerance);
            }
        }

        // Exporters may encode staff lines as <line>, <polyline> or <polygon>.
        foreach (var line in document.Descendants(Svg + "line"))
        {
            if (IsDefinitionElement(line)) continue;
            var transform = SvgPathGeometry.ReadTransformChain(line);
            var p1 = transform.Apply(Parse((string?)line.Attribute("x1")), Parse((string?)line.Attribute("y1")));
            var p2 = transform.Apply(Parse((string?)line.Attribute("x2")), Parse((string?)line.Attribute("y2")));
            AddHorizontal(horizontal, new PointD(p1.X, p1.Y), new PointD(p2.X, p2.Y), tolerance);
        }

        foreach (var polyline in document.Descendants()
                     .Where(x => x.Name == Svg + "polyline" || x.Name == Svg + "polygon"))
        {
            if (IsDefinitionElement(polyline)) continue;
            var values = ParsePointValues(polyline);
            if (values.Length < 4) continue;

            var transform = SvgPathGeometry.ReadTransformChain(polyline);
            var points = new List<PointD>();
            for (var i = 0; i + 1 < values.Length; i += 2)
            {
                var point = transform.Apply(values[i], values[i + 1]);
                points.Add(new PointD(point.X, point.Y));
            }

            for (var i = 1; i < points.Count; i++)
                AddHorizontal(horizontal, points[i - 1], points[i], tolerance);

            if (polyline.Name == Svg + "polygon" && points.Count > 2)
                AddHorizontal(horizontal, points[^1], points[0], tolerance);
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

            // SVG exports use very different coordinate scales. Reject only clearly
            // degenerate groups; the regularity and horizontal overlap are the real tests.
            if (mean <= tolerance * 2) continue;
            if (spaces.Any(s => Math.Abs(s - mean) > Math.Max(tolerance * 2, mean * 0.08))) continue;
            if (block.Min(x => x.Right) - block.Max(x => x.Left) < Math.Max(100, mean * 8)) continue;

            staves.Add(new Staff(staves.Count,
                block.Max(x => x.Left), block.Min(x => x.Right), block.Select(x => x.Y).ToArray()));
            i += 4;
        }

        return staves;
    }

    private static void AddHorizontal(
        ICollection<(double X1, double X2, double Y)> target,
        PointD p1,
        PointD p2,
        double tolerance)
    {
        if (Math.Abs(p1.Y - p2.Y) > tolerance) return;
        if (Math.Abs(p2.X - p1.X) <= 100) return;
        target.Add((Math.Min(p1.X, p2.X), Math.Max(p1.X, p2.X), (p1.Y + p2.Y) / 2));
    }

    private static double[] ParsePointValues(XElement element) =>
        Number.Matches((string?)element.Attribute("points") ?? string.Empty)
            .Select(x => Parse(x.Value))
            .ToArray();

    private static bool ContainsBarline(string? cssClass) =>
        !string.IsNullOrWhiteSpace(cssClass) &&
        cssClass.Contains("barline", StringComparison.OrdinalIgnoreCase);

    private static bool IsDefinitionElement(XElement element) =>
        element.Ancestors(Svg + "defs").Any() || element.Ancestors(Svg + "symbol").Any();

    private static double Parse(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
}
