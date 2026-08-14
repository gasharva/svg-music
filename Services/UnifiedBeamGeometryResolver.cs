using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers beam membership directly from painted page geometry. A single PDF/SVG path may contain
/// several independent beam strips (primary/secondary/tertiary), so contours are inspected
/// separately and the number of strips touching a stem determines eighth/16th/32nd rhythm.
/// </summary>
public sealed class UnifiedBeamGeometryResolver
{
    private sealed record BeamStrip(IReadOnlyList<PointD> Contour, double Left, double Right, double Top, double Bottom, double Thickness)
    {
        public double CenterY => (Top + Bottom) / 2;
    }

    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;
        var strips = FindBeamStrips(analysis);

        foreach (var staff in analysis.Staves)
        {
            var notes = analysis.Events
                .Where(x => x.StaffIndex == staff.Index)
                .Where(x => x.Kind.Equals("notehead-black", StringComparison.OrdinalIgnoreCase))
                .Where(x => x.StemX.HasValue)
                .ToList();

            var contacts = new Dictionary<RecognizedEvent, List<BeamStrip>>();
            foreach (var strip in strips)
            foreach (var note in notes.Where(x => x.StemX!.Value >= strip.Left - staff.Space * .35 && x.StemX.Value <= strip.Right + staff.Space * .35))
            {
                var stem = FindStem(analysis, note, staff);
                var slice = SliceAtX(strip.Contour, Math.Clamp(note.StemX!.Value, strip.Left, strip.Right));
                if (stem is null || !slice.HasValue) continue;
                if (IntervalDistance(stem.Top, stem.Bottom, slice.Value.Top, slice.Value.Bottom) > Math.Max(staff.Space * .65, strip.Thickness * 1.5)) continue;
                if (!contacts.TryGetValue(note, out var list)) contacts[note] = list = [];
                list.Add(strip);
            }

            foreach (var strip in strips.OrderByDescending(x => x.Right - x.Left))
            {
                var members = contacts.Where(x => x.Value.Contains(strip)).Select(x => x.Key).OrderBy(x => x.StemX).ToList();
                if (members.Count < 2) continue;
                for (var i = 0; i < members.Count; i++)
                {
                    var note = members[i];
                    if (!string.IsNullOrWhiteSpace(note.BeamValue)) continue;
                    note.BeamValue = i == 0 ? "begin" : i == members.Count - 1 ? "end" : "continue";
                }
            }

            foreach (var (note, touching) in contacts)
            {
                var levels = new List<BeamStrip>();
                foreach (var strip in touching.OrderBy(x => x.CenterY))
                    if (levels.Count == 0 || Math.Abs(strip.CenterY - levels[^1].CenterY) > staff.Space * .30) levels.Add(strip);

                var beamCount = Math.Clamp(levels.Count, 1, 4);
                note.BeamCount = Math.Max(note.BeamCount, beamCount);
                note.Type = beamCount switch { 1 => "eighth", 2 => "16th", 3 => "32nd", _ => "64th" };
                var baseDuration = DurationForType(note.Type, config.Divisions);
                note.Duration = note.Dotted ? baseDuration * 3 / 2 : baseDuration;
            }
        }
    }

    private static IReadOnlyList<BeamStrip> FindBeamStrips(AnalysisResult analysis)
    {
        var result = new List<BeamStrip>();
        foreach (var path in analysis.DirectPaths)
        foreach (var contour in path.Geometry.Contours)
        {
            if (contour.Count is < 3 or > 10) continue;
            var left = contour.Min(x => x.X); var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y); var bottom = contour.Max(x => x.Y);
            var width = right - left; var height = bottom - top;
            var staff = analysis.Staves
                .Where(s => right >= s.Left - s.Space * 3 && left <= s.Right + s.Space * 3)
                .OrderBy(s => Math.Abs((top + bottom) / 2 - s.Center) / Math.Max(s.Space, .001)).FirstOrDefault();
            if (staff is null) continue;
            if (width < staff.Space * 1.15 || height > staff.Space * 2.0) continue;
            if (width / Math.Max(height, staff.Space * .05) < 1.45) continue;
            var area = PolygonArea(contour);
            var thickness = area / Math.Max(Math.Sqrt(width * width + height * height), .001);
            if (thickness < staff.Space * .05 || thickness > staff.Space * .62) continue;
            result.Add(new BeamStrip(contour, left, right, top, bottom, thickness));
        }
        return result;
    }

    private static SvgLineSegment? FindStem(AnalysisResult analysis, RecognizedEvent note, Staff staff) => analysis.LineSegments
        .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .18)
        .Where(x => x.Height >= staff.Space * 1.10 && x.Height <= staff.Space * 16.0)
        .Where(x => x.Top <= note.Y + staff.Space * .90 && x.Bottom >= note.Y - staff.Space * .90)
        .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value)).ThenBy(x => Math.Abs(x.CenterY - note.Y)).FirstOrDefault();

    private static (double Top, double Bottom)? SliceAtX(IReadOnlyList<PointD> contour, double x)
    {
        var ys = new List<double>();
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i]; var b = contour[(i + 1) % contour.Count];
            var minX = Math.Min(a.X, b.X); var maxX = Math.Max(a.X, b.X);
            if (x < minX - .001 || x > maxX + .001) continue;
            var dx = b.X - a.X;
            if (Math.Abs(dx) < .001) { if (Math.Abs(x - a.X) <= .02) { ys.Add(a.Y); ys.Add(b.Y); } continue; }
            var t = (x - a.X) / dx;
            if (t is >= 0 and <= 1) ys.Add(a.Y + (b.Y - a.Y) * t);
        }
        return ys.Count < 2 ? null : (ys.Min(), ys.Max());
    }

    private static double PolygonArea(IReadOnlyList<PointD> contour)
    {
        if (contour.Count < 3) return 0;
        double twiceArea = 0;
        for (var i = 0; i < contour.Count; i++) { var a = contour[i]; var b = contour[(i + 1) % contour.Count]; twiceArea += a.X * b.Y - b.X * a.Y; }
        return Math.Abs(twiceArea) / 2;
    }

    private static double IntervalDistance(double a1, double a2, double b1, double b2)
    {
        var aTop = Math.Min(a1, a2); var aBottom = Math.Max(a1, a2); var bTop = Math.Min(b1, b2); var bBottom = Math.Max(b1, b2);
        if (aBottom >= bTop && bBottom >= aTop) return 0;
        return Math.Min(Math.Abs(aBottom - bTop), Math.Abs(bBottom - aTop));
    }

    private static int DurationForType(string type, int divisions) => type switch
    {
        "eighth" => Math.Max(1, divisions / 2), "16th" => Math.Max(1, divisions / 4),
        "32nd" => Math.Max(1, divisions / 8), "64th" => Math.Max(1, divisions / 16), _ => Math.Max(1, divisions)
    };
}
