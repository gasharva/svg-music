using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers very compact standalone slurs that the general arc detector deliberately excludes to
/// avoid confusing notehead contours with curves. Real PDF-derived SVG can keep several slurs in a
/// compound direct path; one page-2 slur is only ~1.29 staff spaces wide in painted geometry even
/// though it semantically joins two successive notes about 3.3 spaces apart.
/// </summary>
public sealed class CompactStandaloneArcResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        var notes = analysis.Events
            .Where(x => x.Step is not null && x.StaffIndex >= 0)
            .ToList();
        if (notes.Count == 0) return;

        var nextNumber = notes.Select(x => x.SlurNumber ?? 0).DefaultIfEmpty(0).Max() + 1;

        foreach (var path in analysis.DirectPaths.Where(x => x.SymbolId.StartsWith("path:", StringComparison.Ordinal)))
        foreach (var contour in path.Geometry.Contours)
        {
            if (contour.Count is < 16 or > 80) continue;

            var left = contour.Min(x => x.X);
            var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y);
            var bottom = contour.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;
            if (width <= 0 || height <= 0) continue;

            var centerX = (left + right) / 2;
            var centerY = (top + bottom) / 2;
            var staff = analysis.Staves
                .Where(s => centerX >= s.Left - s.Space * 3 && centerX <= s.Right + s.Space * 3)
                .Select(s => new { Staff = s, Distance = Math.Abs(centerY - s.Center) / Math.Max(s.Space, .001) })
                .Where(x => x.Distance <= 5.0)
                .OrderBy(x => x.Distance)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var widthSp = width / staff.Space;
            var heightSp = height / staff.Space;
            if (widthSp is < 1.10 or > 1.55 || heightSp is < .40 or > 1.20) continue;
            if (widthSp / Math.Max(heightSp, .001) < 1.15) continue;

            var staffNotes = notes.Where(x => x.StaffIndex == staff.Index).ToList();
            var starts = staffNotes
                .Where(x => !x.SlurStart && !x.SlurStop)
                .Where(x => x.X <= right)
                .Where(x => left - x.X <= staff.Space * 2.5)
                .ToList();
            var ends = staffNotes
                .Where(x => !x.SlurStart && !x.SlurStop)
                .Where(x => x.X >= left)
                .Where(x => x.X - right <= staff.Space * 2.5)
                .ToList();

            var pair = starts
                .SelectMany(start => ends
                    .Where(end => end.X - start.X >= staff.Space * 2.0)
                    .Select(end => new
                    {
                        Start = start,
                        End = end,
                        Score = Math.Abs(start.X - left) / staff.Space +
                                Math.Abs(end.X - right) / staff.Space +
                                .35 * Math.Abs(start.Y - centerY) / staff.Space +
                                .35 * Math.Abs(end.Y - centerY) / staff.Space
                    }))
                .OrderBy(x => x.Score)
                .FirstOrDefault();

            if (pair is null || pair.Score > 6.0) continue;

            pair.Start.SlurStart = true;
            pair.Start.SlurNumber = nextNumber;
            pair.End.SlurStop = true;
            pair.End.SlurNumber = nextNumber;
            nextNumber++;
        }
    }
}
