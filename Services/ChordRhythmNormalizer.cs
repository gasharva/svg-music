using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Normalizes rhythmic properties inside geometrically reconstructed chords.
/// SVG engraving usually contains one augmentation dot for the whole chord, while
/// MusicXML expects the same dotted duration on every note element of that chord.
/// </summary>
public sealed class ChordRhythmNormalizer
{
    public void Normalize(AnalysisResult analysis)
    {
        foreach (var staff in analysis.Staves)
        {
            var notes = analysis.Events
                .Where(x => x.StaffIndex == staff.Index && x.Step is not null && x.StemX.HasValue)
                .ToList();

            if (notes.Count == 0) continue;

            var tolerance = staff.Space * .20;

            // Coincident up/down stems are parallel voices, not one chord. PDF/SVG engravers can
            // place those stems at virtually the same X, so StemX alone is not a safe cluster key.
            foreach (var directionGroup in notes.GroupBy(x => x.StemDirection ?? string.Empty))
            {
                var clusters = new List<List<RecognizedEvent>>();

                foreach (var note in directionGroup.OrderBy(x => x.StemX))
                {
                    var cluster = clusters.LastOrDefault();
                    if (cluster is null ||
                        Math.Abs(note.StemX!.Value - cluster.Average(x => x.StemX!.Value)) > tolerance)
                    {
                        clusters.Add([note]);
                    }
                    else
                    {
                        cluster.Add(note);
                    }
                }

                foreach (var chord in clusters.Where(x => x.Count > 1))
                {
                    // A single visible augmentation dot belongs to the rhythmic event, not
                    // to one particular pitch. If any notehead in the shared-stem chord was
                    // associated with a dot, propagate that rhythm to every chord member.
                    if (!chord.Any(x => x.Dotted)) continue;

                    var dottedDuration = chord.Where(x => x.Dotted).Select(x => x.Duration).DefaultIfEmpty(0).Max();
                    if (dottedDuration <= 0)
                        dottedDuration = chord.Max(x => x.Duration);

                    // Prefer the type already carried by a dotted member. In a valid chord all
                    // noteheads represent the same duration, but this also repairs partial
                    // recognition where only one member received the rhythmic annotation.
                    var dottedType = chord.FirstOrDefault(x => x.Dotted && !string.IsNullOrWhiteSpace(x.Type))?.Type
                                     ?? chord.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Type))?.Type;

                    foreach (var note in chord)
                    {
                        note.Dotted = true;
                        note.Duration = dottedDuration;
                        if (!string.IsNullOrWhiteSpace(dottedType)) note.Type = dottedType;
                    }
                }
            }
        }
    }
}
