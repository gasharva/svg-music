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
    private static readonly Regex Matrix = new(@"matrix\(([^)]+)\)", RegexOptions.Compiled);

    public XDocument Load(string path) => XDocument.Load(path, LoadOptions.PreserveWhitespace);

    public List<SvgUse> ReadUses(XDocument document)
    {
        return document.Descendants(Svg + "use")
            .Select(x => new SvgUse(
                ((string?)x.Attribute(XLink + "href") ?? (string?)x.Attribute("href") ?? "").TrimStart('#'),
                Parse((string?)x.Attribute("x")),
                Parse((string?)x.Attribute("y"))))
            .Where(x => !string.IsNullOrWhiteSpace(x.SymbolId))
            .ToList();
    }

    public Dictionary<string, int> CountSymbols(XDocument document) => ReadUses(document)
        .GroupBy(x => x.SymbolId)
        .OrderByDescending(x => x.Count())
        .ToDictionary(x => x.Key, x => x.Count());

    public List<Staff> DetectStaves(XDocument document, double tolerance = 0.25)
    {
        var horizontal = new List<(double X1, double X2, double Y)>();

        foreach (var path in document.Descendants(Svg + "path"))
        {
            var d = (string?)path.Attribute("d");
            if (string.IsNullOrWhiteSpace(d)) continue;

            var transform = ParseMatrix((string?)path.Attribute("transform"));
            foreach (var segment in ReadAxisAlignedSegments(d))
            {
                var p1 = transform.Apply(segment.X1, segment.Y1);
                var p2 = transform.Apply(segment.X2, segment.Y2);
                if (Math.Abs(p1.Y - p2.Y) <= tolerance && Math.Abs(p2.X - p1.X) > 100)
                    horizontal.Add((Math.Min(p1.X, p2.X), Math.Max(p1.X, p2.X), (p1.Y + p2.Y) / 2));
            }
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

    private static IEnumerable<(double X1, double Y1, double X2, double Y2)> ReadAxisAlignedSegments(string d)
    {
        var tokens = Regex.Matches(d, @"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")
            .Select(x => x.Value).ToArray();
        var i = 0; double x = 0, y = 0, sx = 0, sy = 0; char cmd = ' ';
        while (i < tokens.Length)
        {
            if (char.IsLetter(tokens[i][0])) cmd = tokens[i++][0];
            var relative = char.IsLower(cmd);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                {
                    if (i + 1 >= tokens.Length) yield break;
                    var nx = Parse(tokens[i++]); var ny = Parse(tokens[i++]);
                    x = relative ? x + nx : nx; y = relative ? y + ny : ny; sx = x; sy = y;
                    cmd = relative ? 'l' : 'L';
                    break;
                }
                case 'L':
                {
                    if (i + 1 >= tokens.Length) yield break;
                    var nx = Parse(tokens[i++]); var ny = Parse(tokens[i++]);
                    nx = relative ? x + nx : nx; ny = relative ? y + ny : ny;
                    yield return (x, y, nx, ny); x = nx; y = ny; break;
                }
                case 'H':
                {
                    if (i >= tokens.Length) yield break;
                    var nx = Parse(tokens[i++]); nx = relative ? x + nx : nx;
                    yield return (x, y, nx, y); x = nx; break;
                }
                case 'V':
                {
                    if (i >= tokens.Length) yield break;
                    var ny = Parse(tokens[i++]); ny = relative ? y + ny : ny;
                    yield return (x, y, x, ny); y = ny; break;
                }
                case 'Z': x = sx; y = sy; break;
                default:
                    // Кривые для поиска станов не нужны. Пропускаем числа до следующей команды.
                    while (i < tokens.Length && !char.IsLetter(tokens[i][0])) i++;
                    break;
            }
        }
    }

    private static Affine ParseMatrix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Affine.Identity;
        var match = Matrix.Match(value);
        if (!match.Success) return Affine.Identity;
        var v = Number.Matches(match.Groups[1].Value).Select(x => Parse(x.Value)).ToArray();
        return v.Length == 6 ? new Affine(v[0], v[1], v[2], v[3], v[4], v[5]) : Affine.Identity;
    }

    private static double Parse(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private readonly record struct Affine(double A, double B, double C, double D, double E, double F)
    {
        public static Affine Identity => new(1, 0, 0, 1, 0, 0);
        public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);
    }
}
