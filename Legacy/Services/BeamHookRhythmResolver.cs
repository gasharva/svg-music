using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Detects secondary beam levels near a stem end. A compound SVG path may contain both the
/// primary beam and a short secondary hook, so contours are analyzed independently.
/// </summary>
public sealed class BeamHookRhythmResolver
{
    private sealed record Shape(double Left, double Top, double Right, double Bottom, double Thickness)
    {
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

            if (stem is not null && note.BeamCount > 0 && note.StemDirection is "up" or "down")
            {
                var beamEndY = note.StemDirection == "down" ? stem.Bottom : stem.Top;
                var bands = beamLikeShapes
                    .Select(shape => new
                    {
                        Shape = shape,
                        HorizontalGap = HorizontalDistance(note.StemX!.Value, shape.Left, shape.Right),
                        InwardOffset = note.StemDirection == "down"
                            ? beamEndY - shape.CenterY
                            : shape.CenterY - beamEndY
                    })
                    .Where(x => x.HorizontalGap <= staff.Space * .68)
                    .Where(x => x.InwardOffset >= -staff.Space * .32 && x.InwardOffset <= staff.Space * 1.55)
                    .Where(x => IntervalDistance(stem.Top, stem.Bottom, x.Shape.Top, x.Shape.Bottom) <=
                                Math.Max(staff.Space * .65, x.Shape.Thickness * 1.8))
                    .OrderBy(x => x.InwardOffset)
                    .ToList();

                if (bands.Count > 1)
                {
                    var primary = bands[0].InwardOffset;
                    var hasSecondary = bands.Skip(1).Any(x =>
                        x.InwardOffset - primary >= staff.Space * .22);
                    if (hasSecondary)
                    {
                        // This resolver can add a missing secondary hook, but it must never
                        // downgrade a stronger result produced by UnifiedBeamGeometryResolver.
                        // In particular, a 32nd (3 beam levels) must stay a 32nd here.
                        note.BeamCount = Math.Max(2, note.BeamCount);
                    }
                }
            }

            if (note.BeamCount > 0)
            {
                var inferredType = TypeForBeamCount(note.BeamCount);
                if (BeamDepth(note.Type) < note.BeamCount)
                    note.Type = inferredType;

                var baseDuration = DurationForType(note.Type ?? inferredType, config.Divisions);
                note.Duration = note.Dotted ? baseDuration * 3 / 2 : baseDuration;
            }
        }
    }

    private static List<Shape> FindBeamLikeShapes(AnalysisResult analysis)
    {
        var result = new List<Shape>();

        foreach (var path in analysis.DirectPaths)
        foreach (var contour in path.Geometry.Contours)
        {
            if (contour.Count is < 3 or > 30) continue;

            var left = contour.Min(x => x.X);
            var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y);
            var bottom = contour.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;

            var staff = analysis.Staves
                .Where(s => right >= s.Left - s.Space * 3 && left <= s.Right + s.Space * 3)
                .OrderBy(s => Math.Abs((top + bottom) / 2 - s.Center) / Math.Max(s.Space, .001))
                .FirstOrDefault();
            if (staff is null) continue;

            if (width < staff.Space * .08 || width > staff.Space * 20) continue;
            if (height < staff.Space * .04 || height > staff.Space * 1.55) continue;
            if (width / Math.Max(height, staff.Space * .03) < 1.10) continue;

            var area = PolygonArea(contour);
            var longAxis = Math.Sqrt(width * width + height * height);
            var thickness = area / Math.Max(longAxis, .001);
            if (thickness < staff.Space * .04 || thickness > staff.Space * .68) continue;

            result.Add(new Shape(left, top, right, bottom, thickness));
        }

        return result;
    }

    private static int BeamDepth(string? type) => type?.ToLowerInvariant() switch
    {
        "eighth" => 1,
        "16th" => 2,
        "32nd" => 3,
        "64th" => 4,
        _ => 0
    };

    private static string TypeForBeamCount(int beamCount) => beamCount switch
    {
        <= 1 => "eighth",
        2 => "16th",
        3 => "32nd",
        _ => "64th"
    };

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
        "64th" => Math.Max(1, divisions / 16),
        _ => Math.Max(1, divisions)
    };
}
