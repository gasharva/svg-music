using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SvgSymbolScaler;

public sealed class SvgPrintPostProcessor(double protectAbove, double cropPadding)
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly HashSet<string> Supported = ["path", "use", "circle", "ellipse", "rect", "polygon", "polyline"];
    private static readonly Regex TransformRegex = new(@"(?<name>matrix|translate|scale|rotate)\s*\((?<args>[^)]*)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumberRegex = new(@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    public PostProcessResult Process(string path)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("SVG root is missing.");
        var ids = root.DescendantsAndSelf()
            .Select(x => (Element: x, Id: (string?)x.Attribute("id")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id!, x => x.Element, StringComparer.Ordinal);

        var firstStaffY = FindFirstStaffY(root, ids);
        var protectedCount = 0;

        if (firstStaffY is double staffY)
        {
            foreach (var wrapper in root.Descendants(Svg + "g")
                         .Where(x => (string?)x.Attribute("data-svg-symbol-scaler") == "compact")
                         .ToList())
            {
                var child = wrapper.Elements().SingleOrDefault();
                var bounds = child is null ? null : ReadWorldBounds(child, root, ids);
                if (bounds is null || bounds.Value.Bottom >= staffY - protectAbove) continue;
                wrapper.ReplaceWith(child!);
                protectedCount++;
            }
        }

        Bounds? content = null;
        foreach (var element in root.Descendants()
                     .Where(x => Supported.Contains(x.Name.LocalName))
                     .Where(x => !x.Ancestors(Svg + "defs").Any() &&
                                 !x.Ancestors(Svg + "symbol").Any() &&
                                 !x.Ancestors(Svg + "clipPath").Any() &&
                                 !x.Ancestors(Svg + "mask").Any())
                     .Where(IsVisiblePaint))
        {
            var bounds = ReadWorldBounds(element, root, ids);
            if (bounds is not null) content = Bounds.Union(content, bounds);
        }

        if (content is null) throw new InvalidOperationException($"No graphical content found in {path}");
        var crop = Inflate(content.Value, cropPadding);
        root.SetAttributeValue("viewBox", FormattableString.Invariant($"{crop.X:0.########} {crop.Y:0.########} {crop.Width:0.########} {crop.Height:0.########}"));
        root.SetAttributeValue("width", crop.Width.ToString("0.########", CultureInfo.InvariantCulture));
        root.SetAttributeValue("height", crop.Height.ToString("0.########", CultureInfo.InvariantCulture));
        root.SetAttributeValue("preserveAspectRatio", "xMinYMin meet");
        document.Save(path, SaveOptions.DisableFormatting);
        return new PostProcessResult(protectedCount, crop.Width, crop.Height);
    }

    private static double? FindFirstStaffY(XElement root, IReadOnlyDictionary<string, XElement> ids)
    {
        var values = root.Descendants()
            .Where(x => ((string?)x.Attribute("class") ?? "").Contains("StaffLines", StringComparison.OrdinalIgnoreCase))
            .Select(x => ReadWorldBounds(x, root, ids))
            .Where(x => x.HasValue)
            .Select(x => x!.Value.Y)
            .ToArray();
        return values.Length == 0 ? null : values.Min();
    }

    private static bool IsVisiblePaint(XElement element)
    {
        if (string.Equals((string?)element.Attribute("display"), "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals((string?)element.Attribute("visibility"), "hidden", StringComparison.OrdinalIgnoreCase) ||
            Number(element, "opacity", 1) <= 0)
            return false;

        var fill = ((string?)element.Attribute("fill") ?? "black").Trim();
        var stroke = ((string?)element.Attribute("stroke") ?? "none").Trim();
        var fillOpacity = Number(element, "fill-opacity", 1);
        var strokeOpacity = Number(element, "stroke-opacity", 1);
        var fillVisible = fillOpacity > 0 && !IsNoneOrWhite(fill);
        var strokeVisible = strokeOpacity > 0 && !string.Equals(stroke, "none", StringComparison.OrdinalIgnoreCase) && !IsWhite(stroke);
        return fillVisible || strokeVisible;
    }

    private static bool IsNoneOrWhite(string value) =>
        string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) || IsWhite(value);

    private static bool IsWhite(string value)
    {
        var v = value.Replace(" ", "").ToLowerInvariant();
        return v is "white" or "#fff" or "#ffffff" or "rgb(255,255,255)";
    }

    private static Bounds? ReadWorldBounds(XElement element, XElement root, IReadOnlyDictionary<string, XElement> ids)
    {
        var local = ReadLocalBounds(element, root, ids);
        if (local is null) return null;
        return TransformBounds(local.Value, ReadTransformChain(element, root));
    }

    private static Bounds? ReadLocalBounds(XElement element, XElement root, IReadOnlyDictionary<string, XElement> ids) => element.Name.LocalName switch
    {
        "circle" => FromExtents(Number(element,"cx")-Number(element,"r"), Number(element,"cy")-Number(element,"r"), Number(element,"cx")+Number(element,"r"), Number(element,"cy")+Number(element,"r")),
        "ellipse" => FromExtents(Number(element,"cx")-Number(element,"rx"), Number(element,"cy")-Number(element,"ry"), Number(element,"cx")+Number(element,"rx"), Number(element,"cy")+Number(element,"ry")),
        "rect" => new Bounds(Number(element,"x"), Number(element,"y"), Number(element,"width"), Number(element,"height")),
        "polygon" or "polyline" => PointsBounds((string?)element.Attribute("points")),
        "path" => PathBoundsReader.Read((string?)element.Attribute("d")),
        "use" => UseBounds(element, root, ids),
        _ => null
    };

    private static Bounds? UseBounds(XElement use, XElement root, IReadOnlyDictionary<string, XElement> ids)
    {
        XNamespace xlink = "http://www.w3.org/1999/xlink";
        var href = ((string?)use.Attribute(xlink + "href") ?? (string?)use.Attribute("href") ?? "").TrimStart('#');
        if (!ids.TryGetValue(href, out var referenced)) return null;

        Bounds? result = null;
        var viewBox = Numbers((string?)referenced.Attribute("viewBox"));
        if (viewBox.Length == 4)
            result = new Bounds(viewBox[0], viewBox[1], viewBox[2], viewBox[3]);
        else
        {
            foreach (var child in referenced.Descendants().Where(x => Supported.Contains(x.Name.LocalName) && x.Name.LocalName != "use"))
            {
                var childBounds = ReadLocalBounds(child, root, ids);
                if (childBounds is null) continue;
                result = Bounds.Union(result, TransformBounds(childBounds.Value, ReadTransformChain(child, referenced)));
            }
        }
        return result?.Translate(Number(use,"x"), Number(use,"y"));
    }

    private static SvgAffine ReadTransformChain(XElement element, XElement stopAt)
    {
        var chain = element.AncestorsAndSelf().TakeWhile(x => x != stopAt.Parent).Reverse();
        var result = SvgAffine.Identity;
        foreach (var item in chain)
            result = result.Then(ParseTransform((string?)item.Attribute("transform")));
        return result;
    }

    private static SvgAffine ParseTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SvgAffine.Identity;
        var result = SvgAffine.Identity;
        foreach (Match match in TransformRegex.Matches(value))
        {
            var v = NumberRegex.Matches(match.Groups["args"].Value)
                .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
            SvgAffine next = match.Groups["name"].Value.ToLowerInvariant() switch
            {
                "matrix" when v.Length == 6 => new SvgAffine(v[0], v[1], v[2], v[3], v[4], v[5]),
                "translate" when v.Length >= 1 => SvgAffine.Translate(v[0], v.Length > 1 ? v[1] : 0),
                "scale" when v.Length >= 1 => SvgAffine.Scale(v[0], v.Length > 1 ? v[1] : v[0]),
                "rotate" when v.Length >= 1 => v.Length >= 3
                    ? SvgAffine.Translate(v[1], v[2]).Then(SvgAffine.Rotate(v[0])).Then(SvgAffine.Translate(-v[1], -v[2]))
                    : SvgAffine.Rotate(v[0]),
                _ => SvgAffine.Identity
            };
            result = result.Then(next);
        }
        return result;
    }

    private static Bounds TransformBounds(Bounds b, SvgAffine transform)
    {
        var points = new[]
        {
            transform.Apply(b.X, b.Y), transform.Apply(b.Right, b.Y),
            transform.Apply(b.Right, b.Bottom), transform.Apply(b.X, b.Bottom)
        };
        return Bounds.FromExtents(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    private static Bounds? PointsBounds(string? value)
    {
        var n = Numbers(value);
        if (n.Length < 4) return null;
        var xs = new List<double>(); var ys = new List<double>();
        for (var i=0;i+1<n.Length;i+=2) { xs.Add(n[i]); ys.Add(n[i+1]); }
        return FromExtents(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private static Bounds Inflate(Bounds b,double padding) => new(b.X-padding,b.Y-padding,b.Width+padding*2,b.Height+padding*2);
    private static Bounds? FromExtents(double left,double top,double right,double bottom) => right >= left && bottom >= top ? Bounds.FromExtents(left,top,right,bottom) : null;
    private static double Number(XElement e,string name,double fallback=0) => double.TryParse((string?)e.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out var v)?v:fallback;
    private static double[] Numbers(string? value) => string.IsNullOrWhiteSpace(value) ? [] : NumberRegex.Matches(value).Select(x=>double.Parse(x.Value,CultureInfo.InvariantCulture)).ToArray();
}

public readonly record struct PostProcessResult(int Protected, double CropWidth, double CropHeight);

internal readonly record struct SvgPoint(double X, double Y);

internal readonly record struct SvgAffine(double A, double B, double C, double D, double E, double F)
{
    public static SvgAffine Identity => new(1,0,0,1,0,0);
    public static SvgAffine Translate(double x,double y) => new(1,0,0,1,x,y);
    public static SvgAffine Scale(double x,double y) => new(x,0,0,y,0,0);
    public static SvgAffine Rotate(double degrees)
    {
        var r=degrees*Math.PI/180.0; var c=Math.Cos(r); var s=Math.Sin(r);
        return new SvgAffine(c,s,-s,c,0,0);
    }
    public SvgPoint Apply(double x,double y) => new(A*x+C*y+E,B*x+D*y+F);
    public SvgAffine Then(SvgAffine n) => new(n.A*A+n.C*B,n.B*A+n.D*B,n.A*C+n.C*D,n.B*C+n.D*D,n.A*E+n.C*F+n.E,n.B*E+n.D*F+n.F);
}
