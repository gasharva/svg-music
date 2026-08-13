using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class QuarterRestGeometryResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        if (analysis.Staves.Count == 0) return;

        var quarterDuration = analysis.Events
            .Where(x => x.Type == "quarter" && x.Duration > 0)
            .Select(x => x.Duration)
            .DefaultIfEmpty(16)
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .First();

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var geometry = analysis.PageGeometry
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceSymbolId))
            .GroupBy(x => x.SourceSymbolId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (var use in analysis.Uses.Where(x => x.SourceKind == "use"))
        {
            if (!classes.TryGetValue(use.SymbolId, out var cls)) continue;
            if (!cls.Kind.Equals("smufl-unknown", StringComparison.OrdinalIgnoreCase)) continue;
            if (!geometry.TryGetValue(use.SymbolId, out var painted)) continue;
            if (painted.Geometry.Contours.Count != 1) continue;

            var contour = painted.Geometry.Contours[0];
            if (contour.Count is < 250 or > 430) continue;
            if (cls.WidthInSpaces is < .80 or > 1.55) continue;
            if (cls.HeightInSpaces is < 2.25 or > 2.95) continue;

            var staff = analysis.Staves
                .Where(s => use.X >= s.Left - s.Space * 1.5 && use.X <= s.Right + s.Space * 1.5)
                .Select(s => new { Staff = s, Delta = (use.Y - s.Center) / Math.Max(s.Space, .001) })
                .Where(x => x.Delta is >= -5.0 and <= 2.5)
                .OrderBy(x => Math.Abs(x.Delta + 1.8))
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            if (analysis.Events.Any(x => x.StaffIndex == staff.Index &&
                                         x.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase) &&
                                         Math.Abs(x.X - use.X) <= staff.Space * .4))
                continue;

            analysis.Events.Add(new RecognizedEvent
            {
                SourceSymbolId = use.SymbolId,
                Kind = "rest-quarter",
                ReferenceId = cls.ReferenceId,
                Confidence = cls.Score,
                X = use.X,
                Y = use.Y,
                StaffIndex = staff.Index,
                Type = "quarter",
                Duration = quarterDuration
            });
        }
    }
}
