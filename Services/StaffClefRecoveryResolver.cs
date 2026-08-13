using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers clefs that the generic classifier recognized geometrically but that were filtered out
/// of semantic events by the normal confidence threshold. Clefs are accepted only in the left edge
/// slot of a staff, so this remains independent of exporter-specific symbol ids.
/// </summary>
public sealed class StaffClefRecoveryResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        if (analysis.Staves.Count == 0 || analysis.Uses.Count == 0) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);

        foreach (var staff in analysis.Staves)
        {
            if (analysis.Events.Any(x => x.StaffIndex == staff.Index &&
                                         x.Kind.StartsWith("clef-", StringComparison.OrdinalIgnoreCase)))
                continue;

            var candidate = analysis.Uses
                .Where(use => use.X >= staff.Left - staff.Space * .8 && use.X <= staff.Left + staff.Space * 4.5)
                .Where(use => use.Y >= staff.Top - staff.Space * 4.0 && use.Y <= staff.Bottom + staff.Space * 4.0)
                .Select(use => new { Use = use, Class = classes.GetValueOrDefault(use.SymbolId) })
                .Where(x => x.Class is not null &&
                            (x.Class.Kind.Equals("clef-treble", StringComparison.OrdinalIgnoreCase) ||
                             x.Class.Kind.Equals("clef-bass", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.Use.X)
                .ThenByDescending(x => x.Class!.Score)
                .FirstOrDefault();

            if (candidate?.Class is null) continue;

            var treble = candidate.Class.Kind.Equals("clef-treble", StringComparison.OrdinalIgnoreCase);
            analysis.Events.Add(new RecognizedEvent
            {
                SourceSymbolId = candidate.Use.SymbolId,
                Kind = candidate.Class.Kind,
                ReferenceId = candidate.Class.ReferenceId,
                Confidence = candidate.Class.Score,
                X = candidate.Use.X,
                Y = candidate.Use.Y,
                StaffIndex = staff.Index,
                ClefSign = treble ? "G" : "F",
                ClefLine = treble ? 2 : 4
            });

            analysis.Warnings.Add(
                $"Recovered {candidate.Class.Kind} at staff {staff.Index} from staff-left geometry " +
                $"(score {candidate.Class.Score:F3}).");
        }
    }
}
