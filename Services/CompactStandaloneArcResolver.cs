using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers compact slurs that the general arc detector can confuse with beam polygons. PDF-derived
/// compound paths use both dense 20+ point curves and simplified 9-15 point curves.
/// </summary>
public sealed class CompactStandaloneArcResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        var notes = analysis.Events.Where(x => x.Step is not null && x.StaffIndex >= 0).ToList();
        if (notes.Count == 0) return;
        var nextNumber = notes.Select(x => x.SlurNumber ?? 0).DefaultIfEmpty(0).Max() + 1;

        foreach (var path in analysis.DirectPaths.Where(x => x.SymbolId.StartsWith("path:", StringComparison.Ordinal)))
        foreach (var contour in path.Geometry.Contours)
        {
            if (contour.Count is < 9 or > 80) continue;
            var left = contour.Min(x => x.X); var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y); var bottom = contour.Max(x => x.Y);
            var width = right - left; var height = bottom - top;
            if (width <= 0 || height <= 0) continue;

            var centerX = (left + right) / 2; var centerY = (top + bottom) / 2;
            var staff = analysis.Staves
                .Where(s => centerX >= s.Left - s.Space * 3 && centerX <= s.Right + s.Space * 3)
                .Select(s => new { Staff = s, Distance = Math.Abs(centerY - s.Center) / Math.Max(s.Space, .001) })
                .Where(x => x.Distance <= 5.0).OrderBy(x => x.Distance).Select(x => x.Staff).FirstOrDefault();
            if (staff is null) continue;

            var widthSp = width / staff.Space; var heightSp = height / staff.Space;
            var denseCompact = contour.Count >= 16 && widthSp is >= 1.10 and <= 1.55 && heightSp is >= .40 and <= 1.20;
            var simplifiedArc = contour.Count < 16 && widthSp is >= 2.5 and <= 5.2 && heightSp is >= .65 and <= 1.8;
            if (!denseCompact && !simplifiedArc) continue;
            if (widthSp / Math.Max(heightSp, .001) < 1.8) continue;

            var staffNotes = notes.Where(x => x.StaffIndex == staff.Index).ToList();
            var starts = staffNotes
                .Where(x => !x.SlurStart && !x.SlurStop)
                .Where(x => x.X >= left - staff.Space * 1.5 && x.X <= right + staff.Space * 1.5).ToList();
            var ends = staffNotes
                .Where(x => !x.SlurStart && !x.SlurStop)
                .Where(x => x.X >= left - staff.Space * .5 && x.X <= right + staff.Space * 5.0).ToList();

            var pair = starts.SelectMany(start => ends
                    .Where(end => end.X - start.X >= staff.Space * 2.0)
                    .Select(end => new { Start = start, End = end,
                        Score = Math.Abs(start.X - left) / staff.Space + Math.Abs(end.X - right) / staff.Space
                              + .25 * Math.Abs(start.Y - centerY) / staff.Space + .25 * Math.Abs(end.Y - centerY) / staff.Space }))
                .OrderBy(x => x.Score).FirstOrDefault();
            if (pair is null || pair.Score > 7.5) continue;

            pair.Start.SlurStart = true; pair.Start.SlurNumber = nextNumber;
            pair.End.SlurStop = true; pair.End.SlurNumber = nextNumber;
            nextNumber++;
        }
    }
}
