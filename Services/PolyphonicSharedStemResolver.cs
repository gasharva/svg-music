using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Disambiguates two independent voices whose opposite stems are engraved on almost the same X.
/// A common piano engraving pattern places an up-stem quarter note directly over a down-stem
/// dotted-half chord. Looking only at stem X collapses both voices into one chord; the real SVG
/// still contains two disjoint vertical segments, one ending at the upper note and one starting
/// at the lower chord. Endpoint proximity recovers which segment owns each notehead.
/// </summary>
public sealed class PolyphonicSharedStemResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        if (analysis.Staves.Count == 0 || analysis.LineSegments.Count == 0) return;

        var notes = analysis.Events
            .Where(x => x.Step is not null && x.StaffIndex >= 0 && x.StaffIndex < analysis.Staves.Count)
            .ToList();

        foreach (var staffGroup in notes.GroupBy(x => x.StaffIndex))
        {
            var staff = analysis.Staves[staffGroup.Key];
            var stemLines = analysis.LineSegments
                .Where(x => x.Width <= staff.Space * .16)
                .Where(x => x.Height >= staff.Space * 1.35 && x.Height <= staff.Space * 11.0)
                .Where(x => x.CenterX >= staff.Left - staff.Space * 1.5 && x.CenterX <= staff.Right + staff.Space * 1.5)
                .ToList();

            foreach (var note in staffGroup)
            {
                var candidates = stemLines
                    .Where(x => Math.Abs(x.CenterX - note.X) <= staff.Space * 1.12)
                    .Where(x => x.Top <= note.Y + staff.Space * .75 && x.Bottom >= note.Y - staff.Space * .75)
                    .Select(x => new
                    {
                        Line = x,
                        Dx = Math.Abs(x.CenterX - note.X),
                        EndpointGap = Math.Min(Math.Abs(x.Top - note.Y), Math.Abs(x.Bottom - note.Y))
                    })
                    .OrderBy(x => x.Dx)
                    .ToList();

                if (candidates.Count < 2) continue;

                // Only intervene when there are genuinely separate stems on essentially the same
                // horizontal axis. This avoids changing ordinary nearby notes in homophonic music.
                var sameAxis = candidates
                    .Where(x => Math.Abs(x.Line.CenterX - candidates[0].Line.CenterX) <= staff.Space * .08)
                    .ToList();
                if (sameAxis.Count < 2) continue;

                var best = sameAxis
                    .OrderBy(x => x.EndpointGap)
                    .ThenBy(x => x.Dx)
                    .First();

                // At least one endpoint must actually terminate at the notehead. Inner chord
                // members are intentionally left on their existing shared stem assignment.
                if (best.EndpointGap > staff.Space * .90) continue;

                // MusicGeometryRelationResolver may already have selected the correct X but then
                // overwritten its direction while clustering all near-identical X stems together.
                // Therefore endpoint ownership is authoritative here even when StemX barely moves.
                note.StemX = best.Line.CenterX;
                var upExtent = Math.Max(0, note.Y - best.Line.Top);
                var downExtent = Math.Max(0, best.Line.Bottom - note.Y);
                note.StemDirection = upExtent >= downExtent ? "up" : "down";
            }

            RebuildVoiceAwareChords(staffGroup.ToList(), staff);
        }
    }

    private static void RebuildVoiceAwareChords(IReadOnlyList<RecognizedEvent> notes, Staff staff)
    {
        foreach (var note in notes) note.Chord = false;

        var tolerance = staff.Space * .20;
        foreach (var directionGroup in notes
                     .Where(x => x.StemX.HasValue)
                     .GroupBy(x => x.StemDirection ?? string.Empty))
        {
            var clusters = new List<List<RecognizedEvent>>();
            foreach (var note in directionGroup.OrderBy(x => x.StemX))
            {
                var cluster = clusters.LastOrDefault();
                if (cluster is null || Math.Abs(note.StemX!.Value - cluster.Average(x => x.StemX!.Value)) > tolerance)
                    clusters.Add([note]);
                else
                    cluster.Add(note);
            }

            foreach (var cluster in clusters.Where(x => x.Count > 1))
            {
                var ordered = cluster.OrderByDescending(x => x.Y).ToList();
                for (var i = 1; i < ordered.Count; i++) ordered[i].Chord = true;
            }
        }
    }
}
