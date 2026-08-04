using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class SvgPathGeometry
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly Regex TokenRegex = new(@"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);
    private static readonly Regex TransformRegex = new(@"(?<name>matrix|translate|scale)\s*\((?<args>[^)]*)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumberRegex = new(@"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    public Dictionary<string, SymbolGeometry> ReadSymbols(XDocument document, int curveSteps = 12)
    {
        var result = new Dictionary<string, SymbolGeometry>(StringComparer.Ordinal);
        foreach (var symbol in document.Descendants(Svg + "symbol"))
        {
            var id = (string?)symbol.Attribute("id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var contours = new List<IReadOnlyList<PointD>>();
            foreach (var path in symbol.Descendants(Svg + "path"))
            {
                var d = (string?)path.Attribute("d");
                if (string.IsNullOrWhiteSpace(d)) continue;
                var local = Parse(d, curveSteps);
                var transform = ReadTransformChain(path, symbol);
                contours.AddRange(Apply(local, transform));
            }
            if (contours.Count > 0) result[id] = new SymbolGeometry(id, contours);
        }
        return result;
    }

    public IReadOnlyList<SvgDirectPath> ReadDirectPaths(XDocument document, int curveSteps = 12)
    {
        var result = new List<SvgDirectPath>();
        var index = 0;

        foreach (var path in document.Descendants(Svg + "path"))
        {
            if (path.Ancestors(Svg + "symbol").Any() || path.Ancestors(Svg + "defs").Any())
                continue;

            var d = (string?)path.Attribute("d");
            if (string.IsNullOrWhiteSpace(d)) continue;
            var contours = Parse(d, curveSteps);
            if (contours.Count == 0) continue;

            var worldContours = Apply(contours, ReadTransformChain(path));
            var all = worldContours.SelectMany(x => x).ToArray();
            if (all.Length == 0) continue;

            var id = $"path:{index++:D6}";
            var minX = all.Min(x => x.X);
            var maxX = all.Max(x => x.X);
            var minY = all.Min(x => x.Y);
            var maxY = all.Max(x => x.Y);
            result.Add(new SvgDirectPath(
                id,
                new SymbolGeometry(id, worldContours),
                (minX + maxX) / 2.0,
                (minY + maxY) / 2.0));
        }

        return result;
    }

    public Dictionary<string, SymbolGeometry> ReadScoreGeometries(XDocument document, int curveSteps = 12)
    {
        var result = ReadSymbols(document, curveSteps);
        foreach (var path in ReadDirectPaths(document, curveSteps))
            result[path.SymbolId] = path.Geometry;
        return result;
    }

    public SymbolGeometry ReadStandaloneSvg(string path, int curveSteps = 12)
    {
        var doc = XDocument.Load(path);
        var contours = new List<IReadOnlyList<PointD>>();
        foreach (var element in doc.Descendants(Svg + "path"))
        {
            var d = (string?)element.Attribute("d");
            if (string.IsNullOrWhiteSpace(d)) continue;
            contours.AddRange(Apply(Parse(d, curveSteps), ReadTransformChain(element)));
        }
        if (contours.Count == 0) throw new InvalidOperationException($"В эталоне нет path: {path}");
        return new SymbolGeometry(Path.GetFileNameWithoutExtension(path), contours);
    }

    public static ShapeDescriptor Describe(SymbolGeometry geometry, int maxPoints = 256)
    {
        var all = geometry.Contours.SelectMany(x => x).ToList();
        if (all.Count == 0) throw new InvalidOperationException($"Пустая геометрия {geometry.Id}");
        var minX = all.Min(p => p.X); var maxX = all.Max(p => p.X);
        var minY = all.Min(p => p.Y); var maxY = all.Max(p => p.Y);
        var width = Math.Max(maxX - minX, 1e-9); var height = Math.Max(maxY - minY, 1e-9);
        double area = 0, perimeter = 0; int closed = 0;
        foreach (var c in geometry.Contours)
        {
            if (c.Count < 2) continue;
            for (var i = 1; i < c.Count; i++) perimeter += Distance(c[i - 1], c[i]);
            if (Distance(c[0], c[^1]) < Math.Max(width, height) * 0.02)
            {
                closed++;
                for (var i = 0; i < c.Count - 1; i++) area += c[i].X * c[i + 1].Y - c[i + 1].X * c[i].Y;
            }
        }
        area /= 2.0;
        var normalized = all.Select(p => new PointD((p.X - minX) / width, (p.Y - minY) / height)).ToList();
        if (normalized.Count > maxPoints)
        {
            var step = normalized.Count / (double)maxPoints;
            normalized = Enumerable.Range(0, maxPoints).Select(i => normalized[(int)(i * step)]).ToList();
        }
        return new ShapeDescriptor(width, height, width / height, area, Math.Min(1, Math.Abs(area) / (width * height)), perimeter, closed, normalized);
    }

    public static SvgAffine ReadTransformChain(XElement element, XElement? stopBefore = null)
    {
        var chain = element.AncestorsAndSelf().TakeWhile(x => x != stopBefore).Reverse();
        var result = SvgAffine.Identity;
        foreach (var item in chain)
            result = result.Then(ParseTransform((string?)item.Attribute("transform")));
        return result;
    }

    public static SvgAffine ParseTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SvgAffine.Identity;
        var result = SvgAffine.Identity;
        foreach (Match match in TransformRegex.Matches(value))
        {
            var values = NumberRegex.Matches(match.Groups["args"].Value)
                .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
            SvgAffine next = match.Groups["name"].Value.ToLowerInvariant() switch
            {
                "matrix" when values.Length == 6 => new SvgAffine(values[0], values[1], values[2], values[3], values[4], values[5]),
                "translate" when values.Length >= 1 => SvgAffine.Translate(values[0], values.Length > 1 ? values[1] : 0),
                "scale" when values.Length >= 1 => SvgAffine.Scale(values[0], values.Length > 1 ? values[1] : values[0]),
                _ => SvgAffine.Identity
            };
            result = result.Then(next);
        }
        return result;
    }

    private static List<IReadOnlyList<PointD>> Apply(IEnumerable<IReadOnlyList<PointD>> contours, SvgAffine transform) =>
        contours.Select(c => (IReadOnlyList<PointD>)c.Select(transform.Apply).ToArray()).ToList();

    private static List<IReadOnlyList<PointD>> Parse(string d, int curveSteps)
    {
        var t = TokenRegex.Matches(d).Select(x => x.Value).ToArray();
        var contours = new List<IReadOnlyList<PointD>>(); var current = new List<PointD>();
        var i = 0; var cmd = ' '; double x = 0, y = 0, sx = 0, sy = 0; double? lastC2X = null, lastC2Y = null;
        bool HasNumber() => i < t.Length && !char.IsLetter(t[i][0]);
        double N() => double.Parse(t[i++], CultureInfo.InvariantCulture);
        void Add(double px, double py) { x = px; y = py; current.Add(new PointD(x, y)); }
        void Finish() { if (current.Count > 1) contours.Add(current.ToArray()); current = []; }
        while (i < t.Length)
        {
            if (char.IsLetter(t[i][0])) cmd = t[i++][0];
            var rel = char.IsLower(cmd); var upper = char.ToUpperInvariant(cmd);
            if (upper == 'Z') { if (current.Count > 0) current.Add(new PointD(sx, sy)); x = sx; y = sy; Finish(); cmd = ' '; continue; }
            if (!HasNumber()) continue;
            switch (upper)
            {
                case 'M': { var nx=N(); var ny=N(); if(rel){nx+=x;ny+=y;} Finish(); Add(nx,ny); sx=x;sy=y; cmd=rel?'l':'L'; lastC2X=lastC2Y=null; break; }
                case 'L': { var nx=N();var ny=N();if(rel){nx+=x;ny+=y;}Add(nx,ny);break; }
                case 'H': { var nx=N();if(rel)nx+=x;Add(nx,y);break; }
                case 'V': { var ny=N();if(rel)ny+=y;Add(x,ny);break; }
                case 'C': {
                    var x1=N();var y1=N();var x2=N();var y2=N();var ex=N();var ey=N();
                    if(rel){x1+=x;y1+=y;x2+=x;y2+=y;ex+=x;ey+=y;} var ox=x;var oy=y;
                    for(int s=1;s<=curveSteps;s++){var u=s/(double)curveSteps;var v=1-u;Add(v*v*v*ox+3*v*v*u*x1+3*v*u*u*x2+u*u*u*ex,v*v*v*oy+3*v*v*u*y1+3*v*u*u*y2+u*u*u*ey);} lastC2X=x2; lastC2Y=y2; break; }
                case 'S': {
                    var x2=N();var y2=N();var ex=N();var ey=N();
                    if(rel){x2+=x;y2+=y;ex+=x;ey+=y;}
                    var x1=lastC2X.HasValue ? 2*x-lastC2X.Value : x;
                    var y1=lastC2Y.HasValue ? 2*y-lastC2Y.Value : y;
                    var ox=x;var oy=y;
                    for(int s=1;s<=curveSteps;s++){var u=s/(double)curveSteps;var v=1-u;Add(v*v*v*ox+3*v*v*u*x1+3*v*u*u*x2+u*u*u*ex,v*v*v*oy+3*v*v*u*y1+3*v*u*u*y2+u*u*u*ey);} lastC2X=x2; lastC2Y=y2; break; }
                case 'Q': {
                    var x1=N();var y1=N();var ex=N();var ey=N();if(rel){x1+=x;y1+=y;ex+=x;ey+=y;}var ox=x;var oy=y;
                    for(int s=1;s<=curveSteps;s++){var u=s/(double)curveSteps;var v=1-u;Add(v*v*ox+2*v*u*x1+u*u*ex,v*v*oy+2*v*u*y1+u*u*ey);} lastC2X=lastC2Y=null; break; }
                default: while(HasNumber()) i++; break;
            }
        }
        Finish(); return contours;
    }

    private static double Distance(PointD a, PointD b) => Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
}

public readonly record struct SvgAffine(double A, double B, double C, double D, double E, double F)
{
    public static SvgAffine Identity => new(1, 0, 0, 1, 0, 0);
    public static SvgAffine Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static SvgAffine Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    public PointD Apply(PointD point) => new(A * point.X + C * point.Y + E, B * point.X + D * point.Y + F);
    public PointD Apply(double x, double y) => Apply(new PointD(x, y));

    public SvgAffine Then(SvgAffine next) => new(
        next.A * A + next.C * B,
        next.B * A + next.D * B,
        next.A * C + next.C * D,
        next.B * C + next.D * D,
        next.A * E + next.C * F + next.E,
        next.B * E + next.D * F + next.F);
}
