using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

if (args.Length == 0)
{
    Console.WriteLine("Usage: SvgStructure <svg-file>");
    return;
}

var svgPath = args[0];
var score = SvgStructureReader.Read(svgPath);

foreach (var part in score.Parts)
{
    Console.WriteLine($"part {part.Id}");
    foreach (var measure in part.Measures)
        Console.WriteLine($"  measure {measure.Number,2}: width={measure.Width:F2}");
}

public sealed record ScoreStructure(IReadOnlyList<PartStructure> Parts);
public sealed record PartStructure(string Id, IReadOnlyList<MeasureStructure> Measures);
public sealed record MeasureStructure(int Number, double Width);

public static class SvgStructureReader
{
    private static readonly Regex MoveOrLine = new(
        @"(?<cmd>[ML])\s*(?<x>-?\d+(?:\.\d+)?)\s+(?<y>-?\d+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Matrix = new(
        @"matrix\((?<a>-?\d+(?:\.\d+)?)\s+(?<b>-?\d+(?:\.\d+)?)\s+(?<c>-?\d+(?:\.\d+)?)\s+(?<d>-?\d+(?:\.\d+)?)\s+(?<e>-?\d+(?:\.\d+)?)\s+(?<f>-?\d+(?:\.\d+)?)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ScoreStructure Read(string svgPath)
    {
        var doc = XDocument.Load(svgPath);
        var paths = doc.Descendants().Where(x => x.Name.LocalName == "path");

        var systems = new List<SystemBars>();

        foreach (var path in paths)
        {
            var d = (string?)path.Attribute("d");
            var transform = (string?)path.Attribute("transform");
            if (string.IsNullOrWhiteSpace(d) || string.IsNullOrWhiteSpace(transform))
                continue;

            var matrix = ParseMatrix(transform);
            if (matrix is null)
                continue;

            var segments = ParseSegments(d, matrix.Value).ToList();
            if (segments.Count == 0)
                continue;

            // A system in this SVG is conveniently emitted as one path that contains:
            // - 5 long staff lines for the upper staff,
            // - 5 long staff lines for the lower staff,
            // - vertical barlines spanning both staves.
            var horizontal = segments
                .Where(s => NearlyEqual(s.Y1, s.Y2, 0.05) && Math.Abs(s.X2 - s.X1) > 100)
                .ToList();

            if (horizontal.Count < 10)
                continue;

            var staffLeft = horizontal.Min(s => Math.Min(s.X1, s.X2));
            var staffRight = horizontal.Max(s => Math.Max(s.X1, s.X2));
            var staffTop = horizontal.Min(s => s.Y1);
            var staffBottom = horizontal.Max(s => s.Y1);

            var barXs = segments
                .Where(s => NearlyEqual(s.X1, s.X2, 0.05))
                .Where(s => Math.Min(s.Y1, s.Y2) <= staffTop + 1)
                .Where(s => Math.Max(s.Y1, s.Y2) >= staffBottom - 1)
                .Select(s => s.X1)
                .Where(x => x >= staffLeft - 1 && x <= staffRight + 1)
                .OrderBy(x => x)
                .DistinctByTolerance(0.5)
                .ToList();

            if (barXs.Count < 2)
                continue;

            systems.Add(new SystemBars(barXs));
        }

        systems = systems
            .OrderBy(s => s.BarXs.First())
            .ToList();

        // The source score has two parts with identical measure layout. At this level,
        // SVG geometry gives us one shared bar grid. We deliberately duplicate it into
        // P1/P2 and postpone semantic staff-to-part assignment to a later experiment.
        var widths = systems
            .SelectMany(s => ConsecutiveDifferences(s.BarXs))
            .ToList();

        var measures = widths
            .Select((w, i) => new MeasureStructure(i + 1, w))
            .ToList();

        return new ScoreStructure(new[]
        {
            new PartStructure("P1", measures),
            new PartStructure("P2", measures.Select(m => m with { }).ToList())
        });
    }

    private static IEnumerable<double> ConsecutiveDifferences(IReadOnlyList<double> xs)
    {
        for (var i = 0; i < xs.Count - 1; i++)
            yield return xs[i + 1] - xs[i];
    }

    private static Matrix2D? ParseMatrix(string transform)
    {
        var m = Matrix.Match(transform);
        if (!m.Success)
            return null;

        return new Matrix2D(
            Parse(m, "a"), Parse(m, "b"), Parse(m, "c"),
            Parse(m, "d"), Parse(m, "e"), Parse(m, "f"));
    }

    private static IEnumerable<Segment> ParseSegments(string d, Matrix2D matrix)
    {
        var matches = MoveOrLine.Matches(d);
        if (matches.Count < 2)
            yield break;

        (double X, double Y)? current = null;

        foreach (Match match in matches)
        {
            var point = matrix.Apply(Parse(match, "x"), Parse(match, "y"));
            var cmd = match.Groups["cmd"].Value;

            if (cmd == "M")
            {
                current = point;
                continue;
            }

            if (cmd == "L" && current is not null)
            {
                yield return new Segment(current.Value.X, current.Value.Y, point.X, point.Y);
                current = point;
            }
        }
    }

    private static double Parse(Match m, string group) =>
        double.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    private static bool NearlyEqual(double a, double b, double tolerance) => Math.Abs(a - b) <= tolerance;

    private readonly record struct Matrix2D(double A, double B, double C, double D, double E, double F)
    {
        public (double X, double Y) Apply(double x, double y) =>
            (A * x + C * y + E, B * x + D * y + F);
    }

    private readonly record struct Segment(double X1, double Y1, double X2, double Y2);
    private sealed record SystemBars(IReadOnlyList<double> BarXs);
}

public static class EnumerableExtensions
{
    public static IEnumerable<double> DistinctByTolerance(this IEnumerable<double> source, double tolerance)
    {
        double? previous = null;
        foreach (var value in source)
        {
            if (previous is null || Math.Abs(value - previous.Value) > tolerance)
            {
                yield return value;
                previous = value;
            }
        }
    }
}
