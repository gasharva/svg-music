using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers chords whose single painted stem physically crosses the gap between the two staves of
/// a piano system. These notes must remain on their original staffs in MusicXML, but they share one
/// onset, one stem and one voice/chord unit.
/// </summary>
public sealed class CrossStaffChordResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        var groups = MusicXmlWriter.BuildStaffGroups(analysis);
        var nextId = analysis.Events.Select(x => x.CrossStaffChordId ?? 0).DefaultIfEmpty(0).Max() + 1;

        foreach (var group in groups.Where(x => x.Count == 2))
        {
            var upper = group[0];
            var lower = group[1];
            var space = (upper.Space + lower.Space) / 2;
            if (space <= 0) continue;

            var upperNotes = analysis.Events
                .Where(x => x.StaffIndex == upper.Index && x.Step is not null && x.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var lowerNotes = analysis.Events
                .Where(x => x.StaffIndex == lower.Index && x.Step is not null && x.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var stem in analysis.LineSegments
                         .Where(x => x.Width <= space * .18)
                         .Where(x => x.Height >= space * 8.0 && x.Height <= space * 18.0)
                         .Where(x => x.Top <= upper.Bottom + space * 1.4)
                         .Where(x => x.Bottom >= lower.Top - space * 1.4)
                         .OrderBy(x => x.CenterX))
            {
                var topMembers = NearStem(upperNotes, stem.CenterX, space).ToList();
                var bottomMembers = NearStem(lowerNotes, stem.CenterX, space).ToList();
                if (topMembers.Count == 0 || bottomMembers.Count == 0) continue;

                var members = topMembers.Concat(bottomMembers).ToList();

                // A grand-staff barline also spans both staffs, but it has no cluster of noteheads
                // hugging one side of the line. Real stems sit roughly half a staff-space from the
                // notehead centres.
                var horizontalOffsets = members.Select(x => Math.Abs(x.X - stem.CenterX) / space).ToList();
                if (horizontalOffsets.Average() > .82 || horizontalOffsets.Min() > .70) continue;

                // Do not let the same painted stem create two semantic groups if line extraction
                // produced duplicate segments.
                if (members.Any(x => x.CrossStaffChordId.HasValue)) continue;

                var averageHeadX = members.Average(x => x.X);
                var direction = stem.CenterX >= averageHeadX ? "up" : "down";
                var id = nextId++;

                foreach (var note in members)
                {
                    note.StemX = stem.CenterX;
                    note.StemDirection = direction;
                    note.CrossStaffChordId = id;
                }
            }
        }
    }

    private static IEnumerable<RecognizedEvent> NearStem(
        IEnumerable<RecognizedEvent> notes,
        double stemX,
        double space) => notes
        .Where(x => Math.Abs(x.X - stemX) <= space * .82)
        .OrderBy(x => Math.Abs(x.X - stemX));
}
