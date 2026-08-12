using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Detects secondary beam levels near a stem end. Beam/hook recognition is based on painted strip
/// thickness rather than a very small axis-aligned bbox: in PDF-derived SVG a sloped strip can be
/// about one staff-space tall even though its physical thickness is normal.
/// </summary>
public sealed class BeamHookRhythmResolver
{
    private sealed record Shape(double Left, double Top, double Right, double Bottom, double Thickness)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;
        public double CenterY => (Top + Bottom) / 2;
    }

    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;

        var beamLikeShapes = FindBeamLikeShapes(analysis);
        var notes = analysis.Events
            .Where(x => x.Kind.Equals("notehead-black", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.StemX.HasValue && x.StaffIndex >= 0)
            .ToList();

        foreach (var note in notes)
        {
            var staff = analysis.Staves.FirstOrDefault(x => x.Index == note.StaffIndex);
            if (staff is null) continue;

            var stem = analysis.LineSegments
                .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .18)
                .Where(x => x.Height >= staff.Space * 1.10 && x.Height <= staff.Space * 11.2)
                .Where(x => x.Top <= note.Y + staff.Space * .90 && x.Bottom >= note.Y - staff.Space * .90)
                .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
                .ThenBy(x => Math.Abs(x.CenterY - note.Y))
                .FirstOrDefault();

            if (stem is not null && note.BeamCount > 0)
            {
                var beamEndY = note.StemDirection == "down" ? stem.Bottom : stem.Top;

                var hasSecondaryLevel = beamLikeShapes.Any(shape =>
                {
                    var horizontalGap = HorizontalDistance(note.StemX!.Value, shape.Left, shape.Right);
                    if (horizontalGap > staff.Space * .68) return false;

                    var inwardOffset = note.StemDirection == "down"
                        ? beamEndY - shape.CenterY
                        : shape.CenterY - beamEndY;

                    // Primary beam is at/very near the free end. A second beam or hook is displaced
                    // toward the notehead by roughly a fraction to one staff-space. Keep enough
                    // room for thick/sloped exporter strips, but not for remote ledger/slur shapes.
                    if (inwardOffset < staff.Space * .12 || inwardOffset > staff.Space * 1.45)
                        return false;

                    return IntervalDistance(stem.Top, stem.Bottom, shape.Top, shape.Bottom) <=
                           Math.Max(staff.Space * .65, shape.Thickness * 1.8);
                });

                if (hasSecondaryLevel)
                {
                    note.BeamCount = Math.Max(2, note.BeamCount);
                    note.Type = "16th";
                }
            }

            if (note.BeamCount > 0)
            {
                var baseDuration = DurationForType(note.Type ?? "eighth", config.Divisions);
                note.Duration = note.Dotted ? baseDuration * 3 / 2 : baseDuration;
            }
        }
    }

    private static List<Shape> FindBeamLikeShapes(AnalysisResult analysis)
    {
        var result = new List<Shape>();

        foreach (var path in analysis.DirectPaths)
        {
            var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
            if (points.Length is < 3 or > 30) continue;

            var left = points.Min(x => x.X);
            var right = points.Max(x => x.X);
            var top = points.Min(x => x.Y);
            var bottom = points.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;

            var staff = analysis.Staves
                .Where(s => right >= s.Left - s.Space * 3 && left <= s.Right + s.Space * 3)
                .OrderBy(s => Math.Abs((top + bottom) / 2 - s.Center) / Math.Max(s.Space, .001))
                .FirstOrDefault();
            if (staff is null) continue;

            // Full beams and short secondary hooks are the same painted primitive at different
            // lengths. Axis-aligned height is allowed up to 1.55sp because slope inflates it.
            if (width < staff.Space * .08 || width > staff.Space * 20) continue;
            if (height < staff.Space * .04 || height > staff.Space * 1.55) continue;
            if (width / Math.Max(height, staff.Space * .03) < 1.10) continue;

            var area = path.Geometry.Contours.Sum(PolygonArea);
            var longAxis = Math.Sqrt(width * width + height * height);
            var thickness = area / Math.Max(longAxis, .001);
            if (thickness < staff.Space * .04 || thickness > staff.Space * .68) continue;

            result.Add(new Shape(left, top, right, bottom, thickness));
        }

        return result;
    }

    private static double PolygonArea(IReadOnlyList<PointD> contour)
    {
        if (contour.Count < 3) return 0;
        double twiceArea = 0;
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(twiceArea) / 2;
    }

    private static double HorizontalDistance(double x, double left, double right)
    {
        if (x >= left && x <= right) return 0;
        return Math.Min(Math.Abs(x - left), Math.Abs(x - right));
    }

    private static double IntervalDistance(double a1, double a2, double b1, double b2)
    {
        var aTop = Math.Min(a1, a2);
        var aBottom = Math.Max(a1, a2);
        var bTop = Math.Min(b1, b2);
        var bBottom = Math.Max(b1, b2);
        if (aBottom >= bTop && bBottom >= aTop) return 0;
        return Math.Min(Math.Abs(aBottom - bTop), Math.Abs(bBottom - aTop));
    }

    private static int DurationForType(string type, int divisions) => type switch
    {
        "eighth" => Math.Max(1, divisions / 2),
        "16th" => Math.Max(1, divisions / 4),
        "32nd" => Math.Max(1, divisions / 8),
        _ => Math.Max(1, divisions)
    };
}
