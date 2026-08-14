using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Hollow chord heads can be horizontally displaced by a second and only one of them may touch the
/// painted stem. Propagate that stem to nearby stemless hollow heads so voice reconstruction keeps
/// the whole vertical sonority as one chord.
/// </summary>
public sealed class HollowChordStemRecoveryResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        foreach (var staff in analysis.Staves)
        {
            var hollow = analysis.Events
                .Where(x => x.StaffIndex == staff.Index && x.Kind.Equals("notehead-half", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.X).ToList();

            foreach (var anchor in hollow.Where(x => x.StemX.HasValue).ToList())
            {
                var candidates = hollow
                    .Where(x => !ReferenceEquals(x, anchor) && !x.StemX.HasValue)
                    .Where(x => Math.Abs(x.X - anchor.X) <= staff.Space * 1.30)
                    .Where(x => Math.Abs(x.Y - anchor.Y) <= staff.Space * 6.0)
                    .ToList();
                if (candidates.Count < 1) continue;

                // Sequential half notes are normally separated by several spaces. Requiring a
                // vertical stack of at least three heads makes the relaxed 1.3-space X tolerance
                // safe for displaced seconds inside a chord.
                var cluster = candidates.Append(anchor).ToList();
                if (cluster.Count < 3 || cluster.Max(x => x.Y) - cluster.Min(x => x.Y) < staff.Space * 1.5) continue;

                foreach (var note in candidates)
                {
                    note.StemX = anchor.StemX;
                    note.StemDirection = anchor.StemDirection;
                }
            }
        }
    }
}
