using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Detects secondary beam levels near a stem end.
/// Uses raw geometry only; SVG CSS classes are deliberately ignored.
/// </summary>
public sealed class BeamHookRhythmResolver
{
    private sealed record Shape(double Left, double Top, double Right, double Bottom)
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
                .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .14)
                .Where(x => x.Height >= staff.Space * 1.35 && x.Height <= staff.Space * 7.0)
                .Where(x => x.Top <= note.Y + staff.Space * .7 && x.Bottom >= note.Y - staff.Space * .7)
                .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
                .ThenBy(x => Math.Abs(x.CenterY - note.Y))
                .FirstOrDefault();

            if (stem is not null && note.BeamCount > 0)
            {
                var beamEndY = note.StemDirection == "down" ? stem.Bottom : stem.Top;

                // A secondary beam lies on the notehead side of the primary beam. It does not
                // have to touch the stem pixel-for-pixel: engravers commonly leave a small gap
                // between a short hook and the stem. Therefore use directional Y offset plus a
                // tolerant horizontal distance rather than requiring X overlap.
                var hasSecondaryLevel = beamLikeShapes.Any(shape =>
                {
                    var horizontalGap = HorizontalDistance(note.StemX!.Value, shape.Left, shape.Right);
                    if (horizontalGap > staff.Space * .60) return false;

                    var inwardOffset = note.StemDirection == "down"
                        ? beamEndY - shape.CenterY
                        : shape.CenterY - beamEndY;

                    // Ignore the primary beam itself (near-zero offset), and only accept another
                    // thin horizontal band reasonably close to the same stem end.
                    if (inwardOffset < staff.Space * .16 || inwardOffset > staff.Space * 1.15)
                        return false;

                    return IntervalDistance(stem.Top, stem.Bottom, shape.Top, shape.Bottom) <= staff.Space * .55;
                });

                if (hasSecondaryLevel)
                {
                    note.BeamCount = Math.Max(2, note.BeamCount);
                    note.Type = "16th";
                }
            }

            // Beam recognition may change the base note type after augmentation-dot recognition.
            // Keep the dot in the actual MusicXML duration as well as in the visual <dot/> element.
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
            if (points.Length == 0 || points.Length > 30) continue;

            var left = points.Min(x => x.X);
            var right = points.Max(x => x.X);
            var top = points.Min(x => x.Y);
            var bottom = points.Max(x => x.Y);
            var shape = new Shape(left, top, right, bottom);

            var staff = analysis.Staves
                .Where(s => shape.Right >= s.Left - s.Space * 3 && shape.Left <= s.Right + s.Space * 3)
                .OrderBy(s => Math.Abs(shape.CenterY - s.Center) / Math.Max(s.Space, .001))
                .FirstOrDefault();
            if (staff is null) continue;

            // Keep both full beams and tiny partial hooks. The relation to a concrete stem is
            // decided later from geometry, so there is no need for a brittle minimum hook width.
            if (shape.Width < staff.Space * .08 || shape.Width > staff.Space * 20) continue;
            if (shape.Height < staff.Space * .04 || shape.Height > staff.Space * .95) continue;
            if (shape.Width / Math.Max(shape.Height, .001) < 1.15) continue;

            result.Add(shape);
        }

        return result;
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
