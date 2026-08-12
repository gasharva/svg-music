using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Preserves independent voices when an engraver places an up-stem and a down-stem at almost the
/// same X coordinate. Grouping by StemX alone merges those voices into one chord; that can turn a
/// black quarter head into the dotted-half rhythm of the opposite voice and distort slur layout.
/// </summary>
public sealed class OpposedStemVoiceResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        foreach (var staff in analysis.Staves)
        {
            var notes = analysis.Events
                .Where(x => x.StaffIndex == staff.Index && x.Step is not null && x.StemX.HasValue)
                .ToList();
            if (notes.Count == 0) continue;

            // Re-read direction from the exact painted stem selected for each note. Two stems can
            // differ by only a few hundredths of a SVG unit, so their X values belong to the same
            // visual onset but must not be collapsed before direction is considered.
            foreach (var note in notes)
            {
                var stem = analysis.LineSegments
                    .Where(x => x.Height >= staff.Space * 1.0 && x.Height <= staff.Space * 11.2)
                    .Where(x => x.Width <= staff.Space * .18)
                    .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .10)
                    .Where(x => x.Top <= note.Y + staff.Space * .65 &&
                                x.Bottom >= note.Y - staff.Space * .65)
                    .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
                    .ThenBy(x => EndpointDistance(note.Y, x))
                    .FirstOrDefault();
                if (stem is null) continue;

                var upExtent = Math.Max(0, note.Y - stem.Top);
                var downExtent = Math.Max(0, stem.Bottom - note.Y);
                note.StemDirection = upExtent >= downExtent ? "up" : "down";
                note.StemX = stem.CenterX;
            }

            // Rebuild only stemmed chords. Stemless chord recovery has different geometry and is
            // left untouched. Opposite directions at the same onset are parallel voices, not one
            // shared-stem chord.
            foreach (var note in notes) note.Chord = false;

            foreach (var directionGroup in notes.GroupBy(x => x.StemDirection ?? string.Empty))
            {
                var tolerance = staff.Space * .18;
                var clusters = new List<List<RecognizedEvent>>();
                foreach (var note in directionGroup.OrderBy(x => x.StemX))
                {
                    var cluster = clusters.LastOrDefault();
                    if (cluster is null ||
                        Math.Abs(note.StemX!.Value - cluster.Average(x => x.StemX!.Value)) > tolerance)
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

    private static double EndpointDistance(double y, SvgLineSegment line)
    {
        if (y < line.Top) return line.Top - y;
        if (y > line.Bottom) return y - line.Bottom;
        return Math.Min(y - line.Top, line.Bottom - y);
    }
}
