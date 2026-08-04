using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SvgSymbolScaler;

public sealed class CompactSvgScaler(double scale, double maxSize, double maxAspectRatio)
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly HashSet<string> Supported = ["path", "use", "circle", "ellipse", "rect", "polygon", "polyline"];

    public ScaleResult ProcessFile(string inputPath, string outputPath)
    {
        var document = XDocument.Load(inputPath, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("SVG root element is missing.");
        var ids = root.DescendantsAndSelf()
            .Select(x => (Element: x, Id: (string?)x.Attribute("id")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id!, x => x.Element, StringComparer.Ordinal);

        var scaled = 0;
        var skipped = 0;
        var candidates = root.Descendants()
            .Where(x => Supported.Contains(x.Name.LocalName))
            .Where(x => !x.Ancestors(Svg + "defs").Any() && !x.Ancestors(Svg + "symbol").Any())
            .ToList();

        foreach (var element in candidates)
        {
            var bounds = ReadBounds(element, ids);
            if (bounds is null || !IsCompact(element, bounds.Value))
            {
                skipped++;
                continue;
            }

            WrapWithCenteredScale(element, bounds.Value);
            scaled++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        document.Save(outputPath, SaveOptions.DisableFormatting);
        return new ScaleResult(scaled, skipped);
    }

    private bool IsCompact(XElement element, Bounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > maxSize || bounds.Height > maxSize)
            return false;

        var aspect = Math.Max(bounds.Width / bounds.Height, bounds.Height / bounds.Width);
        if (aspect > maxAspectRatio) return false;

        var className = (string?)element.Attribute("class") ?? "";
        if (className.Contains("StaffLines", StringComparison.OrdinalIgnoreCase) ||
            className.Contains("BarLine", StringComparison.OrdinalIgnoreCase) ||
            className.Contains("Stem", StringComparison.OrdinalIgnoreCase) ||
            className.Contains("Beam", StringComparison.OrdinalIgnoreCase))
            return false;

        if (element.Name.LocalName is "polyline" &&
            string.Equals((string?)element.Attribute("fill"), "none", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void WrapWithCenteredScale(XElement element, Bounds bounds)
    {
        var cx = bounds.CenterX.ToString("0.########", CultureInfo.InvariantCulture);
        var cy = bounds.CenterY.ToString("0.########", CultureInfo.InvariantCulture);
        var factor = scale.ToString("0.########", CultureInfo.InvariantCulture);
        var originalTransform = (string?)element.Attribute("transform");
        element.Attribute("transform")?.Remove();

        var inner = new XElement(Svg + "g",
            new XAttribute("data-svg-symbol-scaler", "compact"),
            new XAttribute("transform", $"translate({cx} {cy}) scale({factor}) translate(-{cx} -{cy})"));
        element.ReplaceWith(inner);
        inner.Add(element);

        if (!string.IsNullOrWhiteSpace(originalTransform))
        {
            var outer = new XElement(Svg + "g", new XAttribute("transform", originalTransform));
            inner.ReplaceWith(outer);
            outer.Add(inner);
        }
    }

    private static Bounds? ReadBounds(XElement element, IReadOnlyDictionary<string, XElement> ids)
    {
        return element.Name.LocalName switch
        {
            "circle" => CircleBounds(element),
            "ellipse" => EllipseBounds(element),
            "rect" => RectBounds(element),
            "polygon" or "polyline" => PointsBounds((string?)element.Attribute("points")),
            "path" => PathBoundsReader.Read((string?)element.Attribute("d")),
            "use" => UseBounds(element, ids),
            _ => null
        };
    }

    private static Bounds? UseBounds(XElement use, IReadOnlyDictionary<string, XElement> ids)
    {
        var href = ((string?)use.Attribute(XLink + "href") ?? (string?)use.Attribute("href") ?? "").TrimStart('#');
        if (!ids.TryGetValue(href, out var referenced)) return null;
        double x = Number(use, "x"), y = Number(use, "y");

        Bounds? bounds = null;
        var viewBox = ParseNumbers((string?)referenced.Attribute("viewBox"));
        if (viewBox.Length == 4) bounds = new Bounds(viewBox[0], viewBox[1], viewBox[2], viewBox[3]);
        else
        {
            foreach (var child in referenced.Descendants().Where(c => Supported.Contains(c.Name.LocalName) && c.Name.LocalName != "use"))
                bounds = Bounds.Union(bounds, ReadBounds(child, ids));
        }
        return bounds?.Translate(x, y);
    }

    private static Bounds? CircleBounds(XElement e)
    {
        double cx = Number(e, "cx"), cy = Number(e, "cy"), r = Number(e, "r");
        return r > 0 ? new Bounds(cx - r, cy - r, r * 2, r * 2) : null;
    }

    private static Bounds? EllipseBounds(XElement e)
    {
        double cx = Number(e, "cx"), cy = Number(e, "cy"), rx = Number(e, "rx"), ry = Number(e, "ry");
        return rx > 0 && ry > 0 ? new Bounds(cx - rx, cy - ry, rx * 2, ry * 2) : null;
    }

    private static Bounds? RectBounds(XElement e)
    {
        double width = Number(e, "width"), height = Number(e, "height");
        return width > 0 && height > 0 ? new Bounds(Number(e, "x"), Number(e, "y"), width, height) : null;
    }

    private static Bounds? PointsBounds(string? points)
    {
        var values = ParseNumbers(points);
        if (values.Length < 4) return null;
        var xs = new List<double>();
        var ys = new List<double>();
        for (var i = 0; i + 1 < values.Length; i += 2) { xs.Add(values[i]); ys.Add(values[i + 1]); }
        return Bounds.FromExtents(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private static double Number(XElement element, string name) =>
        double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static double[] ParseNumbers(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : Regex.Matches(value, @"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")
            .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
}

public readonly record struct ScaleResult(int Scaled, int Skipped);

internal readonly record struct Bounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public Bounds Translate(double x, double y) => new(X + x, Y + y, Width, Height);
    public static Bounds FromExtents(double minX, double minY, double maxX, double maxY) => new(minX, minY, maxX - minX, maxY - minY);
    public static Bounds? Union(Bounds? a, Bounds? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return FromExtents(Math.Min(a.Value.X, b.Value.X), Math.Min(a.Value.Y, b.Value.Y),
            Math.Max(a.Value.Right, b.Value.Right), Math.Max(a.Value.Bottom, b.Value.Bottom));
    }
}

internal static class PathBoundsReader
{
    private static readonly Regex Tokens = new(@"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    public static Bounds? Read(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        var tokens = Tokens.Matches(data).Select(x => x.Value).ToArray();
        var points = new List<(double X, double Y)>();
        var i = 0;
        var command = ' ';
        double x = 0, y = 0, startX = 0, startY = 0;
        bool HasNumber() => i < tokens.Length && !char.IsLetter(tokens[i][0]);
        double Next() => double.Parse(tokens[i++], CultureInfo.InvariantCulture);
        void Add(double px, double py) { x = px; y = py; points.Add((x, y)); }

        while (i < tokens.Length)
        {
            if (char.IsLetter(tokens[i][0])) command = tokens[i++][0];
            var relative = char.IsLower(command);
            var upper = char.ToUpperInvariant(command);
            if (upper == 'Z') { Add(startX, startY); command = ' '; continue; }
            if (!HasNumber()) continue;

            double Rx(double value) => relative ? x + value : value;
            double Ry(double value) => relative ? y + value : value;
            switch (upper)
            {
                case 'M':
                case 'L':
                case 'T':
                {
                    var nx = Rx(Next()); var ny = Ry(Next()); Add(nx, ny);
                    if (upper == 'M') { startX = x; startY = y; command = relative ? 'l' : 'L'; }
                    break;
                }
                case 'H': Add(Rx(Next()), y); break;
                case 'V': Add(x, Ry(Next())); break;
                case 'C':
                {
                    var x1 = Rx(Next()); var y1 = Ry(Next());
                    var x2 = Rx(Next()); var y2 = Ry(Next());
                    var ex = Rx(Next()); var ey = Ry(Next());
                    points.Add((x1, y1)); points.Add((x2, y2)); Add(ex, ey); break;
                }
                case 'S':
                case 'Q':
                {
                    var x1 = Rx(Next()); var y1 = Ry(Next());
                    var ex = Rx(Next()); var ey = Ry(Next());
                    points.Add((x1, y1)); Add(ex, ey); break;
                }
                case 'A':
                {
                    var rx = Math.Abs(Next()); var ry = Math.Abs(Next());
                    _ = Next(); _ = Next(); _ = Next();
                    var ex = Rx(Next()); var ey = Ry(Next());
                    points.Add((x - rx, y - ry)); points.Add((x + rx, y + ry));
                    points.Add((ex - rx, ey - ry)); points.Add((ex + rx, ey + ry)); Add(ex, ey); break;
                }
                default: i++; break;
            }
        }

        return points.Count == 0 ? null : Bounds.FromExtents(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }
}
