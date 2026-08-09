using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Detects short secondary beam hooks that are too narrow to be treated as ordinary beams.
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

        var shortHooks = FindShortBeamHooks(analysis);
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

            if (stem is not null)
            {
                var beamEndY = note.StemDirection == "down" ? stem.Bottom : stem.Top;
                var hasSecondaryHook = shortHooks.Any(hook =>
                    note.StemX!.Value >= hook.Left - staff.Space * .20 &&
                    note.StemX.Value <= hook.Right + staff.Space * .20 &&
                    Math.Abs(hook.CenterY - beamEndY) <= staff.Space * .70 &&
                    IntervalDistance(stem.Top, stem.Bottom, hook.Top, hook.Bottom) <= staff.Space * .35);

                if (hasSecondaryHook)
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

    private static List<Shape> FindShortBeamHooks(AnalysisResult analysis)
    {
        var result = new List<Shape>();

        foreach (var path in analysis.DirectPaths)
        {
            var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
            if (points.Length == 0 || points.Length > 14) continue;

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

            // Ordinary beams are handled by MusicGeometryRelationResolver. Here we keep only
            // short, thin beam-like polygons: the little second-level hook of a 16th note.
            if (shape.Width < staff.Space * .30 || shape.Width >= staff.Space * 1.40) continue;
            if (shape.Height < staff.Space * .06 || shape.Height > staff.Space * .75) continue;
            if (shape.Width / Math.Max(shape.Height, .001) < 1.8) continue;

            result.Add(shape);
        }

        return result;
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
