using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers a compact outlined-font mp variant that sits immediately after a hairpin tip. The
/// ordinary production-font fallback expects a slightly taller outline and misses this page-2 form.
/// Requiring an unknown glyph right at a detected wedge endpoint makes the relaxed size window safe.
/// </summary>
public sealed class TerminalDynamicGeometryResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        if (analysis.Directions.Count == 0 || analysis.Staves.Count < 2) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var wedges = analysis.Directions
            .Where(x => x.Kind == "wedge" && x.EndX.HasValue)
            .ToList();

        foreach (var wedge in wedges)
        {
            var staff = analysis.Staves.FirstOrDefault(x => x.Index == wedge.StaffIndex);
            if (staff is null) continue;

            var candidate = analysis.Uses
                .Where(x => x.SourceKind == "use")
                .Where(x => x.X >= wedge.EndX!.Value - staff.Space * .5 &&
                            x.X <= wedge.EndX.Value + staff.Space * 3.0)
                .Where(x => Math.Abs(x.Y - wedge.Y) <= staff.Space * 2.0)
                .Select(x => new { Use = x, Class = classes.GetValueOrDefault(x.SymbolId) })
                .Where(x => x.Class is not null &&
                            x.Class.Kind.Equals("smufl-unknown", StringComparison.OrdinalIgnoreCase))
                .Where(x => x.Class!.WidthInSpaces is >= 3.25 and <= 3.70 &&
                            x.Class.HeightInSpaces is >= 1.35 and <= 1.72)
                .OrderBy(x => Math.Abs(x.Use.X - wedge.EndX!.Value))
                .FirstOrDefault();

            if (candidate is null) continue;
            if (analysis.Directions.Any(x => x.Kind == "dynamic" && x.StaffIndex == wedge.StaffIndex &&
                                             Math.Abs(x.X - candidate.Use.X) <= staff.Space * .5))
                continue;

            analysis.Directions.Add(new DirectionMark
            {
                Kind = "dynamic",
                Value = "mp",
                X = candidate.Use.X,
                Y = candidate.Use.Y,
                StaffIndex = wedge.StaffIndex,
                SourceSymbolId = candidate.Use.SymbolId
            });
        }
    }
}
