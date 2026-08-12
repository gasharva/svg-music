using System.Text.RegularExpressions;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed partial class DynamicsGeometryResolver
{
    private sealed record StaffGap(Staff Upper, Staff Lower);

    public void Resolve(AnalysisResult analysis)
    {
        analysis.Directions.Clear();
        if (analysis.Staves.Count < 2) return;

        var gaps = BuildStaffGaps(analysis);
        if (gaps.Count == 0) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        ResolveDynamics(analysis, gaps, classes);
        ResolveHairpins(analysis, gaps);

        var deduped = analysis.Directions
            .OrderBy(x => x.StaffIndex)
            .ThenBy(x => x.X)
            .ThenBy(x => x.Kind)
            .Aggregate(new List<DirectionMark>(), (items, mark) =>
            {
                var duplicate = items.LastOrDefault(x =>
                    x.StaffIndex == mark.StaffIndex &&
                    x.Kind == mark.Kind &&
                    x.Value == mark.Value &&
                    Math.Abs(x.X - mark.X) <= analysis.Staves[mark.StaffIndex].Space * .35);
                if (duplicate is null) items.Add(mark);
                return items;
            });

        analysis.Directions.Clear();
        analysis.Directions.AddRange(deduped);
    }

    private static void ResolveDynamics(AnalysisResult analysis, IReadOnlyList<StaffGap> gaps,
        IReadOnlyDictionary<string, SymbolClassification> classes)
    {
        foreach (var use in analysis.Uses.Where(x => x.SourceKind == "use"))
        {
            var gap = GapForPoint(use.X, use.Y, gaps);
            if (gap is null || !classes.TryGetValue(use.SymbolId, out var cls)) continue;
            var value = ReadClassifiedDynamic(cls) ?? ReadProductionFontDynamic(cls);
            if (value is null) continue;
            analysis.Directions.Add(new DirectionMark
            {
                Kind = "dynamic", Value = value, X = use.X, Y = use.Y,
                StaffIndex = gap.Upper.Index, SourceSymbolId = use.SymbolId
            });
        }
    }

    private static string? ReadClassifiedDynamic(SymbolClassification cls)
    {
        foreach (var source in new[] { cls.Kind, cls.ReferenceId, cls.MusicXmlValue })
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            var compact = NonLetters().Replace(source.ToLowerInvariant(), string.Empty);
            var dynamicIndex = compact.IndexOf("dynamic", StringComparison.Ordinal);
            if (dynamicIndex < 0) continue;
            var tail = compact[(dynamicIndex + "dynamic".Length)..];
            foreach (var value in KnownDynamics)
                if (tail.Contains(value, StringComparison.Ordinal)) return value;
        }
        return null;
    }

    private static string? ReadProductionFontDynamic(SymbolClassification cls)
    {
        var width = cls.WidthInSpaces;
        var height = cls.HeightInSpaces;
        if (width is >= 3.12 and <= 3.58 && height is >= 1.72 and <= 2.16) return "mp";
        if (width is >= 3.22 and <= 3.68 && height is >= 2.32 and <= 2.80) return "pp";
        return null;
    }

    private static void ResolveHairpins(AnalysisResult analysis, IReadOnlyList<StaffGap> gaps)
    {
        foreach (var path in analysis.DirectPaths)
        foreach (var sourceContour in path.Geometry.Contours)
        {
            var contour = NormalizeContour(sourceContour);
            if (contour.Count != 3) continue;
            var left = contour.Min(x => x.X);
            var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y);
            var bottom = contour.Max(x => x.Y);
            var centerX = (left + right) / 2;
            var centerY = (top + bottom) / 2;
            var gap = GapForPoint(centerX, centerY, gaps);
            if (gap is null) continue;
            var space = gap.Upper.Space;
            var width = (right - left) / Math.Max(space, .001);
            var height = (bottom - top) / Math.Max(space, .001);
            if (width is < 5.5 or > 20.0 || height is < .65 or > 2.5) continue;
            var edgeTolerance = space * .28;
            var leftPoints = contour.Count(x => Math.Abs(x.X - left) <= edgeTolerance);
            var rightPoints = contour.Count(x => Math.Abs(x.X - right) <= edgeTolerance);
            string? value = null;
            if (leftPoints == 1 && rightPoints >= 2) value = "crescendo";
            else if (leftPoints >= 2 && rightPoints == 1) value = "diminuendo";
            if (value is null) continue;
            analysis.Directions.Add(new DirectionMark
            {
                Kind = "wedge", Value = value, X = left, EndX = right, Y = centerY,
                StaffIndex = gap.Upper.Index, SourceSymbolId = path.SymbolId
            });
        }
    }

    private static List<PointD> NormalizeContour(IReadOnlyList<PointD> contour)
    {
        var points = contour.ToList();
        if (points.Count > 1 && Distance(points[0], points[^1]) < .01) points.RemoveAt(points.Count - 1);
        return points;
    }

    private static double Distance(PointD a, PointD b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static List<StaffGap> BuildStaffGaps(AnalysisResult analysis)
    {
        var ordered = analysis.Staves.OrderBy(x => x.Center).ToList();
        var result = new List<StaffGap>();
        for (var i = 0; i + 1 < ordered.Count; i += 2)
        {
            var upper = ordered[i];
            var lower = ordered[i + 1];
            if (lower.Top - upper.Bottom < upper.Space * 2.0) continue;
            result.Add(new StaffGap(upper, lower));
        }
        return result;
    }

    private static StaffGap? GapForPoint(double x, double y, IReadOnlyList<StaffGap> gaps) => gaps
        .Where(g => x >= Math.Min(g.Upper.Left, g.Lower.Left) - g.Upper.Space * 1.5 &&
                    x <= Math.Max(g.Upper.Right, g.Lower.Right) + g.Upper.Space * 1.5)
        .Where(g => y >= g.Upper.Bottom + g.Upper.Space * .35 && y <= g.Lower.Top - g.Upper.Space * .35)
        .OrderBy(g => Math.Abs(y - (g.Upper.Bottom + g.Lower.Top) / 2))
        .FirstOrDefault();

    private static readonly string[] KnownDynamics =
    [
        "pppppp", "ffffff", "ppppp", "fffff", "pppp", "ffff", "ppp", "fff",
        "sffz", "sfpp", "sfz", "sfp", "rfz", "fp", "fz", "pp", "mp", "mf", "ff", "p", "f"
    ];

    [GeneratedRegex("[^a-z]+", RegexOptions.Compiled)]
    private static partial Regex NonLetters();
}
