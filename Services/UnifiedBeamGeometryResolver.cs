using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers primary beam membership directly from painted page geometry. Real PDF-derived SVGs
/// often encode beams as thick parallelograms whose axis-aligned bbox is about one staff-space
/// tall, so the older thin-box detector rejects them. This resolver uses strip thickness and
/// stem-to-strip contact instead of exporter-specific bbox limits.
/// </summary>
public sealed class UnifiedBeamGeometryResolver
{
    private sealed record BeamStrip(
        SvgDirectPath Path,
        double Left,
        double Right,
        double Top,
        double Bottom,
        double Thickness)
    {
        public double Width => Right - Left;
    }

    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;

        foreach (var beam in FindBeamStrips(analysis))
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
                        Stem = FindStem(analysis, note, staff),
                        Slice = SliceAtX(beam.Path, Math.Clamp(note.StemX!.Value, beam.Left, beam.Right))
                    })
                    .Where(x => x.Stem is not null && x.Slice.HasValue)
                    .Where(x => IntervalDistance(
                        x.Stem!.Top, x.Stem.Bottom,
                        x.Slice!.Value.Top, x.Slice.Value.Bottom) <= Math.Max(staff.Space * .65, beam.Thickness * 1.5))
                    .OrderBy(x => x.Note.StemX)
                    .ToList();

                var groups = members
                    .GroupBy(x => Math.Round(x.Note.StemX!.Value / Math.Max(staff.Space * .12, .001)))
                    .OrderBy(x => x.Average(y => y.Note.StemX!.Value))
                    .ToList();

                // A beam is relational evidence. Never classify an isolated thick strip as a beam
                // unless it joins at least two distinct stems.
                if (groups.Count < 2) continue;

                for (var i = 0; i < groups.Count; i++)
                {
                    var beamValue = i == 0 ? "begin" : i == groups.Count - 1 ? "end" : "continue";
                    foreach (var member in groups[i])
                    {
                        var note = member.Note;
                        note.BeamCount = Math.Max(1, note.BeamCount);
                        note.BeamValue = beamValue;

                        if (note.BeamCount == 1)
                            note.Type = "eighth";

                        var baseDuration = DurationForType(note.Type ?? "eighth", config.Divisions);
                        note.Duration = note.Dotted ? baseDuration * 3 / 2 : baseDuration;
                    }
                }
            }
        }
    }

    private static IReadOnlyList<BeamStrip> FindBeamStrips(AnalysisResult analysis)
    {
        var result = new List<BeamStrip>();

        foreach (var path in analysis.DirectPaths)
        {
            var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
            if (points.Length is < 3 or > 24) continue;

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

            if (width < staff.Space * 1.15) continue;
            if (height > staff.Space * 6.0) continue;
            if (width / Math.Max(height, staff.Space * .05) < 1.45) continue;

            // Area / long-axis length estimates the physical strip thickness and is stable for
            // both horizontal and sloped beams. The real source's ~1.0-1.3sp bbox height is mostly
            // slope, while its actual filled-strip thickness remains beam-like.
            var area = path.Geometry.Contours.Sum(PolygonArea);
            var longAxis = Math.Sqrt(width * width + height * height);
            var thickness = area / Math.Max(longAxis, .001);
            if (thickness < staff.Space * .05 || thickness > staff.Space * .62) continue;

            result.Add(new BeamStrip(path, left, right, top, bottom, thickness));
        }

        return result;
    }

    private static SvgLineSegment? FindStem(AnalysisResult analysis, RecognizedEvent note, Staff staff) =>
        analysis.LineSegments
            .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .18)
            .Where(x => x.Height >= staff.Space * 1.10 && x.Height <= staff.Space * 11.0)
            .Where(x => x.Top <= note.Y + staff.Space * .90 && x.Bottom >= note.Y - staff.Space * .90)
            .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
            .ThenBy(x => Math.Abs(x.CenterY - note.Y))
            .FirstOrDefault();

    private static (double Top, double Bottom)? SliceAtX(SvgDirectPath path, double x)
    {
        var ys = new List<double>();
        foreach (var contour in path.Geometry.Contours)
        {
            if (contour.Count < 2) continue;
            for (var i = 0; i < contour.Count; i++)
            {
                var a = contour[i];
                var b = contour[(i + 1) % contour.Count];
                var minX = Math.Min(a.X, b.X);
                var maxX = Math.Max(a.X, b.X);
                if (x < minX - .001 || x > maxX + .001) continue;

                var dx = b.X - a.X;
                if (Math.Abs(dx) < .001)
                {
                    if (Math.Abs(x - a.X) <= .02)
                    {
                        ys.Add(a.Y);
                        ys.Add(b.Y);
                    }
                    continue;
                }

                var t = (x - a.X) / dx;
                if (t is >= 0 and <= 1)
                    ys.Add(a.Y + (b.Y - a.Y) * t);
            }
        }
        return ys.Count < 2 ? null : (ys.Min(), ys.Max());
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
