using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SvgSymbolScaler;

public sealed class SvgPrintPostProcessor(double protectAbove, double cropPadding)
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly HashSet<string> Supported = ["path", "use", "circle", "ellipse", "rect", "polygon", "polyline"];

    public PostProcessResult Process(string path)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("SVG root is missing.");
        var firstStaffY = FindFirstStaffY(root);
        var protectedCount = 0;

        if (firstStaffY is double staffY)
        {
            foreach (var wrapper in root.Descendants(Svg + "g")
                         .Where(x => (string?)x.Attribute("data-svg-symbol-scaler") == "compact")
                         .ToList())
            {
                var child = wrapper.Elements().SingleOrDefault();
                var bounds = child is null ? null : ReadBounds(child, root);
                if (bounds is null || bounds.Value.Bottom >= staffY - protectAbove) continue;
                wrapper.ReplaceWith(child!);
                protectedCount++;
            }
        }

        Bounds? content = null;
        foreach (var element in root.Descendants()
                     .Where(x => Supported.Contains(x.Name.LocalName))
                     .Where(x => !x.Ancestors(Svg + "defs").Any() && !x.Ancestors(Svg + "symbol").Any()))
        {
            var bounds = ReadBounds(element, root);
            if (bounds is null) continue;
            var transform = element.AncestorsAndSelf(Svg + "g")
                .Select(x => (string?)x.Attribute("transform"))
                .FirstOrDefault(x => x?.Contains("scale(", StringComparison.OrdinalIgnoreCase) == true);
            if (transform is not null && TryReadCenteredScale(transform, out var factor))
                bounds = ScaleAroundCenter(bounds.Value, factor);
            content = Bounds.Union(content, bounds);
        }

        if (content is null) throw new InvalidOperationException($"No graphical content found in {path}");
        var crop = Inflate(content.Value, cropPadding);
        root.SetAttributeValue("viewBox", FormattableString.Invariant($"{crop.X:0.########} {crop.Y:0.########} {crop.Width:0.########} {crop.Height:0.########}"));
        root.SetAttributeValue("width", crop.Width.ToString("0.########", CultureInfo.InvariantCulture));
        root.SetAttributeValue("height", crop.Height.ToString("0.########", CultureInfo.InvariantCulture));
        document.Save(path, SaveOptions.DisableFormatting);
        return new PostProcessResult(protectedCount, crop.Width, crop.Height);
    }

    private static double? FindFirstStaffY(XElement root)
    {
        var values = root.Descendants()
            .Where(x => ((string?)x.Attribute("class") ?? "").Contains("StaffLines", StringComparison.OrdinalIgnoreCase))
            .Select(x => ReadBounds(x, root))
            .Where(x => x.HasValue)
            .Select(x => x!.Value.Y)
            .ToArray();
        return values.Length == 0 ? null : values.Min();
    }

    private static bool TryReadCenteredScale(string transform, out double scale)
    {
        var match = Regex.Match(transform, @"scale\s*\(\s*(?<s>[-+]?\d*\.?\d+)", RegexOptions.IgnoreCase);
        return double.TryParse(match.Groups["s"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
    }

    private static Bounds? ReadBounds(XElement element, XElement root) => element.Name.LocalName switch
    {
        "circle" => FromAreaExtents(Number(element,"cx")-Number(element,"r"), Number(element,"cy")-Number(element,"r"), Number(element,"cx")+Number(element,"r"), Number(element,"cy")+Number(element,"r")),
        "ellipse" => FromAreaExtents(Number(element,"cx")-Number(element,"rx"), Number(element,"cy")-Number(element,"ry"), Number(element,"cx")+Number(element,"rx"), Number(element,"cy")+Number(element,"ry")),
        "rect" => new Bounds(Number(element,"x"), Number(element,"y"), Number(element,"width"), Number(element,"height")),
        "polygon" or "polyline" => PointsBounds((string?)element.Attribute("points")),
        "path" => PathBoundsReader.Read((string?)element.Attribute("d")),
        "use" => UseBounds(element, root),
        _ => null
    };

    private static Bounds? UseBounds(XElement use, XElement root)
    {
        XNamespace xlink = "http://www.w3.org/1999/xlink";
        var href = ((string?)use.Attribute(xlink + "href") ?? (string?)use.Attribute("href") ?? "").TrimStart('#');
        var referenced = root.DescendantsAndSelf().FirstOrDefault(x => (string?)x.Attribute("id") == href);
        if (referenced is null) return null;
        Bounds? result = null;
        foreach (var child in referenced.Descendants().Where(x => Supported.Contains(x.Name.LocalName) && x.Name.LocalName != "use"))
            result = Bounds.Union(result, ReadBounds(child, root));
        return result?.Translate(Number(use,"x"), Number(use,"y"));
    }

    private static Bounds? PointsBounds(string? value)
    {
        var n = Numbers(value);
        if (n.Length < 4) return null;
        var xs = new List<double>(); var ys = new List<double>();
        for (var i=0;i+1<n.Length;i+=2) { xs.Add(n[i]); ys.Add(n[i+1]); }
        return Bounds.FromExtents(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private static Bounds ScaleAroundCenter(Bounds b,double factor) => new(b.CenterX-b.Width*factor/2,b.CenterY-b.Height*factor/2,b.Width*factor,b.Height*factor);
    private static Bounds Inflate(Bounds b,double padding) => new(b.X-padding,b.Y-padding,b.Width+padding*2,b.Height+padding*2);
    private static Bounds? FromAreaExtents(double left,double top,double right,double bottom) => right>left && bottom>top ? Bounds.FromExtents(left,top,right,bottom) : null;
    private static double Number(XElement e,string name) => double.TryParse((string?)e.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out var v)?v:0;
    private static double[] Numbers(string? value) => string.IsNullOrWhiteSpace(value) ? [] : Regex.Matches(value,@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?").Select(x=>double.Parse(x.Value,CultureInfo.InvariantCulture)).ToArray();
}

public readonly record struct PostProcessResult(int Protected, double CropWidth, double CropHeight);
