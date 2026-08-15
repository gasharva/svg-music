using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers medium-height slurs drawn above short beamed runs. Their painted endpoints are often
/// attached near stem tips rather than noteheads, so notehead-Y scoring can reject an otherwise
/// obvious arc. This pass is additive and only fills still-unassigned slur endpoints.
/// </summary>
public sealed class StemAnchoredSlurResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        if (analysis.Staves.Count == 0) return;

        var notes = analysis.Events
            .Where(x => x.Step is not null && x.StaffIndex >= 0 && x.BeamCount > 0 && x.StemX.HasValue)
            .ToList();
        if (notes.Count < 2) return;

        var nextNumber = analysis.Events.Select(x => x.SlurNumber ?? 0).DefaultIfEmpty(0).Max() + 1;

        foreach (var path in analysis.DirectPaths)
        foreach (var contour in path.Geometry.Contours)
        {
            if (contour.Count is < 16 or > 90) continue;

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
                .Where(s => centerX >= s.Left - s.Space * 2 && centerX <= s.Right + s.Space * 2)
                .Select(s => new
                {
                    Staff = s,
                    Distance = Math.Abs(centerY - s.Center) / Math.Max(s.Space, .001)
                })
                .Where(x => x.Distance <= 5.5)
                .OrderBy(x => x.Distance)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var widthSp = width / staff.Space;
            var heightSp = height / staff.Space;
            if (widthSp is < 2.5 or > 12 || heightSp is < .55 or > 3.2) continue;

            // This pass is specifically for arcs above a beamed run.
            if (centerY >= staff.Top + staff.Space * .25) continue;

            var leftY = EndpointY(contour, left, staff.Space);
            var rightY = EndpointY(contour, right, staff.Space);
            var middleY = MiddleY(contour, centerX, width, staff.Space);
            if (middleY >= Math.Min(leftY, rightY) - staff.Space * .12) continue;

            var run = notes
                .Where(x => x.StaffIndex == staff.Index)
                .Where(x => x.X >= left - staff.Space * 1.2 && x.X <= right + staff.Space * 1.2)
                .OrderBy(x => x.X)
                .ToList();
            if (run.Count < 2) continue;

            var start = run.OrderBy(x => Math.Abs(x.X - left)).First();
            var end = run.OrderBy(x => Math.Abs(x.X - right)).First();
            if (ReferenceEquals(start, end) || end.X <= start.X) continue;
            if (start.SlurStart || start.SlurStop || end.SlurStart || end.SlurStop) continue;

            var startStem = FindStem(analysis, start, staff);
            var endStem = FindStem(analysis, end, staff);
            if (startStem is null || endStem is null) continue;

            var startTipY = start.StemDirection == "down" ? startStem.Bottom : startStem.Top;
            var endTipY = end.StemDirection == "down" ? endStem.Bottom : endStem.Top;
            if (Math.Abs(startTipY - leftY) > staff.Space * 2.0 ||
                Math.Abs(endTipY - rightY) > staff.Space * 2.0)
                continue;

            start.SlurStart = true;
            start.SlurNumber = nextNumber;
            end.SlurStop = true;
            end.SlurNumber = nextNumber;
            nextNumber++;
        }
    }

    private static SvgLineSegment? FindStem(AnalysisResult analysis, RecognizedEvent note, Staff staff) =>
        analysis.LineSegments
            .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .20)
            .Where(x => x.Height >= staff.Space * 1.1 && x.Height <= staff.Space * 11.2)
            .Where(x => x.Top <= note.Y + staff.Space * .9 && x.Bottom >= note.Y - staff.Space * .9)
            .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
            .ThenBy(x => Math.Abs(x.CenterY - note.Y))
            .FirstOrDefault();

    private static double EndpointY(IReadOnlyList<PointD> points, double endpointX, double staffSpace)
    {
        var tolerance = Math.Max(.01, staffSpace * .04);
        var endpoint = points.Where(x => Math.Abs(x.X - endpointX) <= tolerance).ToList();
        return endpoint.Count > 0
            ? endpoint.Average(x => x.Y)
            : points.OrderBy(x => Math.Abs(x.X - endpointX)).First().Y;
    }

    private static double MiddleY(IReadOnlyList<PointD> points, double centerX, double width, double staffSpace)
    {
        var tolerance = Math.Max(staffSpace * .15, width * .08);
        var middle = points.Where(x => Math.Abs(x.X - centerX) <= tolerance).ToList();
        return middle.Count > 0
            ? middle.Min(x => x.Y)
            : points.OrderBy(x => Math.Abs(x.X - centerX)).First().Y;
    }
}
