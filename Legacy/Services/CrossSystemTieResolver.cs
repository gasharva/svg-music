using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers ties whose painted curve ends at the right edge of a staff system. In engraving a tie
/// that continues on the next system is split visually, so the outgoing SVG arc has no destination
/// note on the same staff. Its semantic destination is the matching pitch near the left edge of the
/// corresponding staff in the next system.
/// </summary>
public sealed class CrossSystemTieResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        var staves = analysis.Staves.OrderBy(x => x.Center).ToList();
        if (staves.Count < 3) return;

        var notes = analysis.Events
            .Where(x => x.Step is not null && x.StaffIndex >= 0)
            .ToList();

        for (var staffOrder = 0; staffOrder + 2 < staves.Count; staffOrder++)
        {
            var staff = staves[staffOrder];
            var nextStaff = staves[staffOrder + 2];

            // Piano systems are paired upper/lower. Only compare the same side of consecutive
            // systems; reject irregular spacing where +2 is clearly not the next corresponding staff.
            if (staffOrder % 2 != (staffOrder + 2) % 2) continue;

            foreach (var path in analysis.DirectPaths.Where(x => x.SymbolId.StartsWith("path:", StringComparison.Ordinal)))
            foreach (var contour in path.Geometry.Contours)
            {
                if (contour.Count is < 12 or > 120) continue;

                var left = contour.Min(x => x.X);
                var right = contour.Max(x => x.X);
                var top = contour.Min(x => x.Y);
                var bottom = contour.Max(x => x.Y);
                var widthSp = (right - left) / Math.Max(staff.Space, .001);
                var heightSp = (bottom - top) / Math.Max(staff.Space, .001);
                if (widthSp is < 8.0 or > 32.0 || heightSp is < .25 or > 3.0) continue;

                // The crucial evidence: this is an open continuation curve that runs to the system edge.
                if (staff.Right - right > staff.Space * .45) continue;
                if (left < staff.Left + staff.Space * 2.0) continue;
                var centerY = (top + bottom) / 2;
                if (centerY < staff.Top - staff.Space * 3.0 || centerY > staff.Bottom + staff.Space * 3.0) continue;

                var start = notes
                    .Where(x => x.StaffIndex == staff.Index && !x.TieStart)
                    .Where(x => Math.Abs(x.X - left) <= staff.Space * 2.0)
                    .Where(x => Math.Abs(x.Y - centerY) <= staff.Space * 2.5)
                    .OrderBy(x => Math.Abs(x.X - left) + .45 * Math.Abs(x.Y - centerY))
                    .FirstOrDefault();
                if (start is null) continue;

                var target = notes
                    .Where(x => x.StaffIndex == nextStaff.Index && !x.TieStop)
                    .Where(x => string.Equals(x.Step, start.Step, StringComparison.Ordinal) && x.Octave == start.Octave)
                    .Where(x => x.X <= nextStaff.Left + nextStaff.Space * 10.0)
                    .Where(x => SameWrittenPitch(start, x))
                    .OrderBy(x => x.X)
                    .FirstOrDefault();
                if (target is null) continue;

                start.TieStart = true;
                target.TieStop = true;
            }
        }
    }

    private static bool SameWrittenPitch(RecognizedEvent a, RecognizedEvent b)
    {
        if (a.Alter == b.Alter) return true;
        // At a system start the accidental may be omitted because notation state carries it.
        return string.IsNullOrWhiteSpace(b.AttachedToSymbolId);
    }
}
