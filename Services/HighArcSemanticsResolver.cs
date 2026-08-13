using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers genuine slurs that live well above the staff. High notation is allowed, but this pass
/// must be additive: it may not steal an endpoint that the normal arc resolver already assigned.
/// That guard is important because an overwritten slur number leaves an unmatched endpoint and
/// notation editors then render spectacular system-wide arcs.
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
                    // This resolver is specifically for notation ABOVE the staff. Using absolute
                    // distance here previously admitted unrelated geometry below neighbouring staves.
                    DistanceAbove = (s.Top - centerY) / Math.Max(s.Space, .001)
                })
                .Where(x => x.DistanceAbove is > 5.0 and <= 12.5)
                .OrderBy(x => x.DistanceAbove)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var widthSp = width / staff.Space;
            var heightSp = height / staff.Space;
            if (widthSp is < 3.0 or > 18.0 || heightSp is < .45 or > 4.8) continue;

            // A high slur must actually arch away from its endpoints. This rejects long, shallow
            // page-decoration/text contours that happen to have a slur-like bounding box.
            var leftY = EndpointY(contour, left, staff.Space);
            var rightY = EndpointY(contour, right, staff.Space);
            var middleY = MiddleY(contour, centerX, width, staff.Space);
            if (middleY >= Math.Min(leftY, rightY) - staff.Space * .15) continue;

            var staffNotes = notes.Where(x => x.StaffIndex == staff.Index).ToList();
            var start = staffNotes
                .Where(x => Math.Abs(x.X - left) <= staff.Space * 2.0)
                .OrderBy(x => Math.Abs(x.X - left))
                .FirstOrDefault();
            var end = staffNotes
                .Where(x => Math.Abs(x.X - right) <= staff.Space * 2.0)
                .OrderBy(x => Math.Abs(x.X - right))
                .FirstOrDefault();

            if (start is null || end is null || ReferenceEquals(start, end)) continue;

            // Displaced chord heads can have slightly different X values even though they belong to
            // the same onset. Requiring a real musical horizontal span prevents self-slurs.
            if (end.X - start.X < staff.Space * 2.5) continue;

            // Do not overwrite an ordinary slur endpoint. The event model currently carries one
            // slur number per note, so overwriting either side would orphan the original pair.
            if (start.SlurStart || start.SlurStop || end.SlurStart || end.SlurStop) continue;

            start.SlurStart = true;
            start.SlurNumber = nextSlurNumber;
            end.SlurStop = true;
            end.SlurNumber = nextSlurNumber;
            nextSlurNumber++;
        }
    }

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
