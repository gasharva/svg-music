using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Re-attaches written accidentals after final staff/pitch reconstruction.
/// In close intervals/chords noteheads are intentionally displaced horizontally, so the staff
/// line/space crossed by the accidental is a stronger cue than smallest X distance.
/// SVG CSS classes are not used; only the symbol classifier result and geometry are consulted.
/// </summary>
public sealed class AccidentalGeometryResolver
{
    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var notes = analysis.Events.Where(x => x.Step is not null).ToList();

        // Remove provisional assignments made before final stem/chord/staff reconstruction.
        foreach (var note in notes)
        {
            note.Alter = 0;
            note.AttachedToSymbolId = null;
        }

        foreach (var use in analysis.Uses)
        {
            if (!classes.TryGetValue(use.SymbolId, out var cls)) continue;
            if (!cls.Kind.StartsWith("accidental-", StringComparison.OrdinalIgnoreCase)) continue;

            var staff = analysis.Staves
                .Where(s => use.X >= s.Left - s.Space * 3 && use.X <= s.Right + s.Space * 3)
                .Select(s => new { Staff = s, Distance = Math.Abs(use.Y - s.Center) / Math.Max(s.Space, .001) })
                .Where(x => x.Distance <= 4.0)
                .OrderBy(x => x.Distance)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var accidentalPosition = StaffPosition(use.Y, staff);
            var target = notes
                .Where(x => x.StaffIndex == staff.Index)
                .Where(x => x.X > use.X)
                .Where(x => x.X - use.X <= staff.Space * config.MaxAttachmentDistanceInSpaces)
                .Where(x => Math.Abs(x.Y - use.Y) <= staff.Space * 1.35)
                .Select(x => new
                {
                    Note = x,
                    PositionDelta = Math.Abs(StaffPosition(x.Y, staff) - accidentalPosition),
                    YDelta = Math.Abs(x.Y - use.Y),
                    XDelta = x.X - use.X
                })
                // First choose the same musical row (line or space). This is the SVG equivalent
                // of asking which staff line/space passes through the accidental. Only then use
                // exact Y and horizontal proximity to break ties.
                .OrderBy(x => x.PositionDelta)
                .ThenBy(x => x.YDelta)
                .ThenBy(x => x.XDelta)
                .Select(x => x.Note)
                .FirstOrDefault();

            if (target is null) continue;

            target.Alter = cls.Kind switch
            {
                "accidental-flat" => -1,
                "accidental-double-flat" => -2,
                "accidental-sharp" => 1,
                "accidental-double-sharp" => 2,
                _ => 0
            };
            target.AttachedToSymbolId = use.SymbolId;
        }
    }

    private static int StaffPosition(double y, Staff staff) =>
        (int)Math.Round((staff.Bottom - y) / Math.Max(staff.Space / 2, .001));
}
