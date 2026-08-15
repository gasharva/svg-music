using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Converts exporter-specific glyph placement anchors into painted horizontal centers for noteheads.
/// PDF-derived SVGs commonly place a <use> at the glyph origin/left side while stems are standalone
/// painted geometry in page coordinates. Comparing those two coordinate systems directly makes a
/// perfectly adjacent stem appear more than one staff-space away.
///
/// Pitch Y intentionally stays on the musical glyph anchor: it already maps correctly to staff
/// positions. Only X is normalized for spatial relation/layout work.
/// </summary>
public sealed class PaintedGlyphPositionNormalizer
{
    public void Normalize(AnalysisResult analysis)
    {
        if (analysis.PageGeometry.Count == 0 || analysis.Staves.Count == 0) return;

        var averageSpace = analysis.Staves.Average(x => x.Space);
        var byIdentity = analysis.PageGeometry
            .GroupBy(x => x.SourceSymbolId ?? x.InstanceId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);

        foreach (var note in analysis.Events.Where(x =>
                     x.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase)))
        {
            if (!byIdentity.TryGetValue(note.SourceSymbolId, out var candidates)) continue;

            var space = note.StaffIndex >= 0 && note.StaffIndex < analysis.Staves.Count
                ? analysis.Staves[note.StaffIndex].Space
                : averageSpace;

            // The original event X/Y are the SVG <use> placement anchor. The matching painted
            // instance is necessarily local to that anchor; this guard prevents a repeated glyph
            // elsewhere on the page from being selected accidentally.
            var match = candidates
                .Where(x => Math.Abs(x.X - note.X) <= space * 1.6)
                .Where(x => Math.Abs(x.Y - note.Y) <= space * 1.6)
                .OrderBy(x => Math.Pow((x.X - note.X) / space, 2) + Math.Pow((x.Y - note.Y) / space, 2))
                .FirstOrDefault();

            if (match is not null)
                note.X = match.X;
        }
    }
}
