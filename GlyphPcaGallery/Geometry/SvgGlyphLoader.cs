using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkiaSharp;

namespace GlyphPcaGallery.Geometry;

public static class SvgGlyphLoader
{
    private static readonly Regex TransformRegex = new(@"([a-zA-Z]+)\s*\(([^)]*)\)", RegexOptions.Compiled);

    public static SKPath? LoadFilledPath(string fileName)
    {
        var document = XDocument.Load(fileName, LoadOptions.PreserveWhitespace);
        var combined = new SKPath { FillType = SKPathFillType.EvenOdd };
        if (document.Root is null) throw new InvalidDataException("SVG has no root element.");
        Visit(document.Root, Affine2D.Identity, combined);
        if (combined.IsEmpty) return null; //throw new InvalidDataException("SVG contains no filled <path> geometry.");
        return combined;
    }

    private static void Visit(XElement element, Affine2D inherited, SKPath combined)
    {
        var cumulative = inherited.Then(ParseTransform((string?)element.Attribute("transform")));

        if (element.Name.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            var d = (string?)element.Attribute("d");
            if (!string.IsNullOrWhiteSpace(d) && IsFilled(element))
            {
                using var parsed = SKPath.ParseSvgPathData(d);
                if (parsed is not null)
                {
                    using var transformed = new SKPath(parsed);
                    transformed.Transform(cumulative.ToSkMatrix());
                    combined.AddPath(transformed);
                }
            }
        }

        foreach (var child in element.Elements()) Visit(child, cumulative, combined);
    }

    private static bool IsFilled(XElement element)
    {
        var fill = ((string?)element.Attribute("fill"))?.Trim();
        if (string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase)) return false;

        var style = (string?)element.Attribute("style");
        if (!string.IsNullOrWhiteSpace(style))
        {
            foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':', 2);
                if (kv.Length == 2 && kv[0].Trim().Equals("fill", StringComparison.OrdinalIgnoreCase)
                    && kv[1].Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
            }
        }
        return true;
    }

    private static Affine2D ParseTransform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Affine2D.Identity;
        var result = Affine2D.Identity;

        foreach (Match match in TransformRegex.Matches(text))
        {
            var name = match.Groups[1].Value.ToLowerInvariant();
            var v = match.Groups[2].Value.Split(new[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();

            Affine2D t = name switch
            {
                "matrix" when v.Length >= 6 => new(v[0], v[2], v[1], v[3], v[4], v[5]),
                "translate" when v.Length >= 1 => new(1, 0, 0, 1, v[0], v.Length >= 2 ? v[1] : 0),
                "scale" when v.Length >= 1 => new(v[0], 0, 0, v.Length >= 2 ? v[1] : v[0], 0, 0),
                "rotate" when v.Length >= 1 => Rotate(v),
                _ => Affine2D.Identity
            };
            result = result.Then(t);
        }
        return result;
    }

    private static Affine2D Rotate(double[] v)
    {
        var radians = v[0] * Math.PI / 180.0;
        var c = Math.Cos(radians); var s = Math.Sin(radians);
        var r = new Affine2D(c, -s, s, c, 0, 0);
        if (v.Length < 3) return r;
        var toOrigin = new Affine2D(1, 0, 0, 1, -v[1], -v[2]);
        var back = new Affine2D(1, 0, 0, 1, v[1], v[2]);
        return toOrigin.Then(r).Then(back);
    }
}
