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
                    if (!chord.Any(x => x.Dotted)) continue;

                    var dottedDuration = chord.Where(x => x.Dotted).Select(x => x.Duration).DefaultIfEmpty(0).Max();
                    if (dottedDuration <= 0)
                        dottedDuration = chord.Max(x => x.Duration);

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
