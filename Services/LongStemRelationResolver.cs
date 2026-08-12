using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Attaches unusually long stems that the ordinary stem candidate filter intentionally excludes.
/// Real engraved passages with a steep/remote beam can produce stems longer than seven staff
/// spaces. To avoid mistaking barlines for stems, this pass only accepts a long vertical line when
/// one of its endpoints is actually adjacent to a notehead.
/// </summary>
public sealed class LongStemRelationResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        if (analysis.Staves.Count == 0) return;

        var averageSpace = analysis.Staves.Average(x => x.Space);
        var longLines = analysis.LineSegments
            .Where(x => x.Width <= averageSpace * .16)
            .Where(x => x.Height > averageSpace * 7.0 && x.Height <= averageSpace * 11.0)
            .ToList();

        if (longLines.Count == 0) return;

        foreach (var note in analysis.Events
                     .Where(x => x.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase))
                     .Where(x => !x.StemX.HasValue))
        {
            var candidate = longLines
                .Select(line => new
                {
                    Line = line,
                    Dx = Math.Abs(line.CenterX - note.X),
                    EndpointGap = Math.Min(Math.Abs(line.Top - note.Y), Math.Abs(line.Bottom - note.Y))
                })
                .Where(x => x.Dx <= averageSpace * 1.12)
                .Where(x => x.EndpointGap <= averageSpace * .90)
                .OrderBy(x => x.Dx)
                .ThenBy(x => x.EndpointGap)
                .FirstOrDefault();

            if (candidate is null) continue;

            var line = candidate.Line;
            note.StemX = line.CenterX;
            note.StemDirection = Math.Abs(line.Top - note.Y) <= Math.Abs(line.Bottom - note.Y)
                ? "down"
                : "up";
        }
    }
}
