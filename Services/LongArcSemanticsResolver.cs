using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers very long tie/slur contours that span most of a system. The ordinary arc resolver caps
/// width at 18 staff spaces to avoid giant false positives; real scores can legitimately contain
/// 25-30sp phrase/tie arcs across adjacent measures. This pass keeps the wider window but requires
/// both painted endpoints to terminate near real noteheads on the same staff.
/// </summary>
public sealed class LongArcSemanticsResolver
{
    public void Resolve(AnalysisResult analysis)
    {
        var notes = analysis.Events
            .Where(x => x.Step is not null && x.StaffIndex >= 0)
            .ToList();
        if (notes.Count == 0) return;

        foreach (var path in analysis.DirectPaths.Where(x => x.SymbolId.StartsWith("path:", StringComparison.Ordinal)))
        foreach (var contour in path.Geometry.Contours)
        {
            if (contour.Count is < 12 or > 100) continue;

            var left = contour.Min(x => x.X);
            var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y);
            var bottom = contour.Max(x => x.Y);
            var centerX = (left + right) / 2;
            var centerY = (top + bottom) / 2;

            var staff = analysis.Staves
                .Where(s => centerX >= s.Left - s.Space * 2 && centerX <= s.Right + s.Space * 2)
                .Select(s => new { Staff = s, Distance = Math.Abs(centerY - s.Center) / Math.Max(s.Space, .001) })
                .Where(x => x.Distance <= 5.5)
                .OrderBy(x => x.Distance)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var widthSp = (right - left) / Math.Max(staff.Space, .001);
            var heightSp = (bottom - top) / Math.Max(staff.Space, .001);
            if (widthSp is < 18.0 or > 36.0) continue;
            if (heightSp is < .35 or > 2.5) continue;
            if (widthSp / Math.Max(heightSp, .001) < 8.0) continue;

            var leftY = EndpointY(contour, left, staff.Space);
            var rightY = EndpointY(contour, right, staff.Space);

            var starts = notes
                .Where(x => x.StaffIndex == staff.Index)
                .Where(x => Math.Abs(x.X - left) <= staff.Space * 2.6)
                .Where(x => Math.Abs(x.Y - leftY) <= staff.Space * 1.45)
                .ToList();
            var ends = notes
                .Where(x => x.StaffIndex == staff.Index)
                .Where(x => Math.Abs(x.X - right) <= staff.Space * 2.6)
                .Where(x => Math.Abs(x.Y - rightY) <= staff.Space * 1.45)
                .ToList();

            var pair = starts
                .SelectMany(start => ends
                    .Where(end => end.X > start.X + staff.Space * 4)
                    .Select(end => new
                    {
                        Start = start,
                        End = end,
                        SamePitch = SameWrittenPitch(start, end),
                        Score = Math.Abs(start.X - left) / staff.Space +
                                Math.Abs(end.X - right) / staff.Space +
                                .8 * Math.Abs(start.Y - leftY) / staff.Space +
                                .8 * Math.Abs(end.Y - rightY) / staff.Space
                    }))
                .OrderByDescending(x => x.SamePitch)
                .ThenBy(x => x.Score)
                .FirstOrDefault();

            if (pair is null || pair.Score > 7.0) continue;

            if (pair.SamePitch)
            {
                if (pair.Start.TieStart || pair.End.TieStop) continue;
                pair.Start.TieStart = true;
                pair.End.TieStop = true;
            }
            else
            {
                if (pair.Start.SlurStart || pair.End.SlurStop) continue;
                var number = analysis.Events.Select(x => x.SlurNumber ?? 0).DefaultIfEmpty().Max() + 1;
                pair.Start.SlurStart = true;
                pair.Start.SlurNumber = number;
                pair.End.SlurStop = true;
                pair.End.SlurNumber = number;
            }
        }
    }

    private static bool SameWrittenPitch(RecognizedEvent a, RecognizedEvent b) =>
        string.Equals(a.Step, b.Step, StringComparison.Ordinal) &&
        a.Octave == b.Octave &&
        (a.Alter == b.Alter || string.IsNullOrWhiteSpace(b.AttachedToSymbolId));

    private static double EndpointY(IReadOnlyList<PointD> points, double endpointX, double staffSpace)
    {
        var tolerance = Math.Max(.01, staffSpace * .02);
        var endpointPoints = points.Where(x => Math.Abs(x.X - endpointX) <= tolerance).ToList();
        return endpointPoints.Count > 0
            ? endpointPoints.Average(x => x.Y)
            : points.OrderBy(x => Math.Abs(x.X - endpointX)).First().Y;
    }
}
