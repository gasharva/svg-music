using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Completes long sloped beam groups after the primary sloped-beam detector has found the beam shape.
/// Uses a fitted beam centreline instead of exact polygon/stem intersection, so edge stems and tiny
/// exporter gaps do not truncate the group. SVG CSS classes are intentionally ignored.
/// </summary>
public sealed class SlopedBeamCoverageResolver
{
    private sealed record BeamModel(
        double Left,
        double Right,
        double Slope,
        double Intercept,
        double Thickness)
    {
        public double YAt(double x) => Slope * x + Intercept;
    }

    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;

        foreach (var beam in FindBeamModels(analysis))
        {
            foreach (var staff in analysis.Staves)
            {
                var members = analysis.Events
                    .Where(x => x.StaffIndex == staff.Index)
                    .Where(x => x.Kind.Equals("notehead-black", StringComparison.OrdinalIgnoreCase))
                    .Where(x => x.StemX.HasValue)
                    .Where(x => x.StemX!.Value >= beam.Left - staff.Space * .35 &&
                                x.StemX.Value <= beam.Right + staff.Space * .35)
                    .Select(note => new
                    {
                        Note = note,
                        Stem = FindStem(analysis, note, staff)
                    })
                    .Where(x => x.Stem is not null)
                    .Where(x => ReachesBeam(x.Note, x.Stem!, beam, staff))
                    .OrderBy(x => x.Note.StemX)
                    .ToList();

                var stemGroups = members
                    .GroupBy(x => Math.Round(x.Note.StemX!.Value / (staff.Space * .12)))
                    .OrderBy(x => x.Average(y => y.Note.StemX!.Value))
                    .ToList();

                if (stemGroups.Count < 2) continue;

                for (var i = 0; i < stemGroups.Count; i++)
                {
                    var beamValue = i == 0 ? "begin" : i == stemGroups.Count - 1 ? "end" : "continue";
                    foreach (var member in stemGroups[i])
                    {
                        var note = member.Note;
                        note.BeamCount = Math.Max(1, note.BeamCount);
                        note.BeamValue = beamValue;

                        // This pass restores only the primary beam. Never downgrade a note that
                        // already has a secondary beam level.
                        if (note.BeamCount == 1)
                            note.Type = "eighth";

                        var baseDuration = DurationForType(note.Type ?? "eighth", config.Divisions);
                        note.Duration = note.Dotted ? baseDuration * 3 / 2 : baseDuration;
                    }
                }
            }
        }
    }

    private static bool ReachesBeam(RecognizedEvent note, SvgLineSegment stem, BeamModel beam, Staff staff)
    {
        var x = note.StemX!.Value;
        var expectedY = beam.YAt(Math.Clamp(x, beam.Left, beam.Right));

        // Stem-up notes meet the beam at their top end; stem-down notes meet it at their bottom end.
        // If direction is unavailable, use whichever end is closer to the fitted beam line.
        var stemEndY = note.StemDirection switch
        {
            "up" => stem.Top,
            "down" => stem.Bottom,
            _ => Math.Abs(stem.Top - expectedY) <= Math.Abs(stem.Bottom - expectedY) ? stem.Top : stem.Bottom
        };

        var tolerance = Math.Max(staff.Space * .70, beam.Thickness * 1.8);
        return Math.Abs(stemEndY - expectedY) <= tolerance;
    }

    private static List<BeamModel> FindBeamModels(AnalysisResult analysis)
    {
        var result = new List<BeamModel>();

        foreach (var path in analysis.DirectPaths)
        {
            var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
            if (points.Length is < 3 or > 14) continue;

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

            if (width < staff.Space * 1.4) continue;
            if (height > staff.Space * 6.0) continue;
            if (width / Math.Max(height, staff.Space * .05) < 1.5) continue;

            var area = path.Geometry.Contours.Sum(PolygonArea);
            var longAxis = Math.Sqrt(width * width + height * height);
            var thickness = area / Math.Max(longAxis, .001);
            if (thickness < staff.Space * .05 || thickness > staff.Space * .55) continue;

            // Least-squares fit through all contour points. Because a beam is a thin strip,
            // fitting all points yields its centreline and is insensitive to small endpoint gaps.
            var meanX = points.Average(p => p.X);
            var meanY = points.Average(p => p.Y);
            var denominator = points.Sum(p => (p.X - meanX) * (p.X - meanX));
            if (denominator <= .001) continue;
            var slope = points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / denominator;
            var intercept = meanY - slope * meanX;

            result.Add(new BeamModel(left, right, slope, intercept, thickness));
        }

        return result;
    }

    private static SvgLineSegment? FindStem(AnalysisResult analysis, RecognizedEvent note, Staff staff) =>
        analysis.LineSegments
            .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .14)
            .Where(x => x.Height >= staff.Space * 1.25 && x.Height <= staff.Space * 7.0)
            .Where(x => x.Top <= note.Y + staff.Space * .75 && x.Bottom >= note.Y - staff.Space * .75)
            .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
            .FirstOrDefault();

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

    private static int DurationForType(string type, int divisions) => type switch
    {
        "eighth" => Math.Max(1, divisions / 2),
        "16th" => Math.Max(1, divisions / 4),
        "32nd" => Math.Max(1, divisions / 8),
        _ => Math.Max(1, divisions)
    };
}
