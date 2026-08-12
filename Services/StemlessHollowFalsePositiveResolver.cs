using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Rejects hollow glyphs from nearby text that were shape-matched as half-note heads.
/// A half note outside the five staff lines must have either its stem or supporting ledger-line
/// geometry. This keeps notation evidence as the guard instead of relying on source symbol ids.
/// </summary>
public sealed class StemlessHollowFalsePositiveResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        var falsePositives = analysis.Events
            .Where(x => x.Kind.Equals("notehead-half", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.StemX.HasValue)
            .Where(x => x.StaffIndex >= 0)
            .Where(x =>
            {
                var staff = analysis.Staves.FirstOrDefault(s => s.Index == x.StaffIndex);
                if (staff is null) return false;

                // Inside the staff, a hollow no-stem glyph may later prove to be a whole note;
                // do not suppress it here. The problematic text glyphs sit clearly above/below.
                var outside = x.Y < staff.Top - staff.Space * .75 ||
                              x.Y > staff.Bottom + staff.Space * .75;
                return outside && !HasLedgerSupport(analysis, x, staff);
            })
            .ToList();

        foreach (var item in falsePositives)
            analysis.Events.Remove(item);
    }

    private static bool HasLedgerSupport(AnalysisResult analysis, RecognizedEvent note, Staff staff) =>
        analysis.LineSegments.Any(line =>
        {
            var horizontal = line.Width >= staff.Space * .75 && line.Height <= staff.Space * .18;
            if (!horizontal) return false;

            var left = Math.Min(line.X1, line.X2);
            var right = Math.Max(line.X1, line.X2);
            var y = (line.Y1 + line.Y2) / 2;
            return note.X >= left - staff.Space * .55 &&
                   note.X <= right + staff.Space * .55 &&
                   Math.Abs(note.Y - y) <= staff.Space * .38;
        });
}
