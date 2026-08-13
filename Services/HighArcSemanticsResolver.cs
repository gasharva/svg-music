using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers slurs that live well above the staff. Dense editions can place an arch 5-12 staff
/// spaces above the notes; the ordinary arc resolver intentionally uses a tighter vertical window.
/// This pass is deliberately narrow geometrically and only accepts a high curved contour when both
/// horizontal endpoints land near actual notes on the same staff.
/// </summary>
public sealed class HighArcSemanticsResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        var notes = analysis.Events
            .Where(x => x.Step is not null && x.StaffIndex >= 0)
            .ToList();
        if (notes.Count == 0) return;

        var nextSlurNumber = notes.Select(x => x.SlurNumber ?? 0).DefaultIfEmpty(0).Max() + 1;

        foreach (var path in analysis.DirectPaths)
        foreach (var contour in path.Geometry.Contours)
        {
            if (contour.Count < 16) continue;

            var left = contour.Min(x => x.X);
            var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y);
            var bottom = contour.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;
            if (width <= 0 || height <= 0 || width / height < 2.0) continue;

            var centerX = (left + right) / 2;
            var centerY = (top + bottom) / 2;
            var staff = analysis.Staves
                .Where(s => centerX >= s.Left - s.Space * 3 && centerX <= s.Right + s.Space * 3)
                .Select(s => new
                {
                    Staff = s,
                    Distance = Math.Abs(centerY - s.Center) / Math.Max(s.Space, .001)
                })
                .Where(x => x.Distance is > 5.0 and <= 12.5)
                .OrderBy(x => x.Distance)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var widthSp = width / staff.Space;
            var heightSp = height / staff.Space;
            if (widthSp is < 3.0 or > 18.0 || heightSp is < .45 or > 4.8) continue;

            var staffNotes = notes.Where(x => x.StaffIndex == staff.Index).ToList();
            var start = staffNotes
                .Where(x => Math.Abs(x.X - left) <= staff.Space * 2.0)
                .OrderBy(x => Math.Abs(x.X - left))
                .FirstOrDefault();
            var end = staffNotes
                .Where(x => Math.Abs(x.X - right) <= staff.Space * 2.0)
                .OrderBy(x => Math.Abs(x.X - right))
                .FirstOrDefault();

            if (start is null || end is null || ReferenceEquals(start, end) || start.X >= end.X) continue;
            if (start.SlurStart && end.SlurStop) continue;

            start.SlurStart = true;
            start.SlurNumber = nextSlurNumber;
            end.SlurStop = true;
            end.SlurNumber = nextSlurNumber;
            nextSlurNumber++;
        }
    }
}
