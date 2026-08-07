using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Restores relationships between already recognized musical symbols from raw SVG geometry.
/// Intentionally ignores SVG CSS classes: only coordinates and shapes are used.
/// </summary>
public sealed class MusicGeometryRelationResolver
{
    private sealed record StemCandidate(int StaffIndex, SvgLineSegment Line);
    private sealed record Box(double Left, double Top, double Right, double Bottom, SvgDirectPath Path)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;
        public double CenterX => (Left + Right) / 2;
        public double CenterY => (Top + Bottom) / 2;
    }

    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        var notes = analysis.Events.Where(x => x.Step is not null).ToList();
        if (notes.Count == 0 || analysis.Staves.Count == 0) return;

        var stems = FindStems(analysis);
        AttachNotesToStems(notes, stems, analysis.Staves);
        RebuildChords(notes, analysis.Staves);

        var beamBoxes = FindBeamShapes(analysis);
        AttachBeams(notes, stems, beamBoxes, analysis.Staves, config);

        var arcs = FindArcShapes(analysis, beamBoxes);
        AttachArcs(notes, arcs, analysis.Staves);
    }

    private static List<StemCandidate> FindStems(AnalysisResult analysis)
    {
        var result = new List<StemCandidate>();
        foreach (var line in analysis.LineSegments)
        {
            foreach (var staff in analysis.Staves)
            {
                // Stem: almost vertical, roughly 2..6.5 staff spaces high. This naturally
                // excludes staff lines, ledger lines and grand-staff barlines.
                if (line.Width > staff.Space * .14) continue;
                if (line.Height < staff.Space * 1.6 || line.Height > staff.Space * 6.5) continue;
                if (line.CenterX < staff.Left - staff.Space || line.CenterX > staff.Right + staff.Space) continue;
                if (line.Bottom < staff.Top - staff.Space * 4 || line.Top > staff.Bottom + staff.Space * 4) continue;
                result.Add(new StemCandidate(staff.Index, line));
                break;
            }
        }
        return result;
    }

    private static void AttachNotesToStems(
        IReadOnlyList<RecognizedEvent> notes,
        IReadOnlyList<StemCandidate> stems,
        IReadOnlyList<Staff> staves)
    {
        foreach (var note in notes)
        {
            var staff = staves[note.StaffIndex];
            var stem = stems
                .Where(x => x.StaffIndex == note.StaffIndex)
                .Where(x => Math.Abs(x.Line.CenterX - note.X) <= staff.Space * 1.05)
                .Where(x => x.Line.Top <= note.Y + staff.Space * .55 && x.Line.Bottom >= note.Y - staff.Space * .55)
                .OrderBy(x => Math.Abs(x.Line.CenterX - note.X))
                .FirstOrDefault();

            if (stem is not null)
                note.StemX = stem.Line.CenterX;
        }
    }

    private static void RebuildChords(IReadOnlyList<RecognizedEvent> notes, IReadOnlyList<Staff> staves)
    {
        foreach (var note in notes) note.Chord = false;

        foreach (var staffGroup in notes.Where(x => x.StemX.HasValue).GroupBy(x => x.StaffIndex))
        {
            var tolerance = staves[staffGroup.Key].Space * .18;
            var clusters = new List<List<RecognizedEvent>>();
            foreach (var note in staffGroup.OrderBy(x => x.StemX))
            {
                var cluster = clusters.LastOrDefault();
                if (cluster is null || Math.Abs(note.StemX!.Value - cluster.Average(x => x.StemX!.Value)) > tolerance)
                    clusters.Add([note]);
                else
                    cluster.Add(note);
            }

            foreach (var cluster in clusters.Where(x => x.Count > 1))
            {
                // MusicXML chord marker belongs to every note after the first one.
                var ordered = cluster.OrderByDescending(x => x.Y).ToList();
                for (var i = 1; i < ordered.Count; i++) ordered[i].Chord = true;
            }
        }

        // Keep the old X-based behavior only for notes for which no stem was found.
        foreach (var staffGroup in notes.Where(x => !x.StemX.HasValue).GroupBy(x => x.StaffIndex))
        {
            var tolerance = staves[staffGroup.Key].Space * .45;
            var ordered = staffGroup.OrderBy(x => x.X).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (Math.Abs(ordered[i].X - ordered[i - 1].X) <= tolerance)
                    ordered[i].Chord = true;
        }
    }

    private static List<Box> FindBeamShapes(AnalysisResult analysis)
    {
        var result = new List<Box>();
        foreach (var path in analysis.DirectPaths)
        {
            var box = Bounds(path);
            var staff = ClosestStaff(box.CenterX, box.CenterY, analysis.Staves, 6);
            if (staff is null) continue;

            var pointCount = path.Geometry.Contours.Sum(x => x.Count);
            if (box.Width < staff.Space * 1.4) continue;
            if (box.Height < staff.Space * .08 || box.Height > staff.Space * .95) continue;
            if (box.Width / Math.Max(box.Height, .001) < 2.2) continue;
            // Beams are polygonal; curved slurs/ties have many sampled points.
            if (pointCount > 14) continue;
            result.Add(box);
        }
        return result;
    }

    private static void AttachBeams(
        IReadOnlyList<RecognizedEvent> notes,
        IReadOnlyList<StemCandidate> stems,
        IReadOnlyList<Box> beams,
        IReadOnlyList<Staff> staves,
        RecognitionConfig config)
    {
        foreach (var note in notes)
        {
            note.BeamValue = null;
            note.BeamCount = 0;
            if (!note.StemX.HasValue || note.Kind != "notehead-black") continue;

            var staff = staves[note.StaffIndex];
            var stem = stems
                .Where(x => x.StaffIndex == note.StaffIndex)
                .OrderBy(x => Math.Abs(x.Line.CenterX - note.StemX.Value))
                .FirstOrDefault();
            if (stem is null) continue;

            var touching = beams.Where(box =>
                    stem.Line.CenterX >= box.Left - staff.Space * .25 &&
                    stem.Line.CenterX <= box.Right + staff.Space * .25 &&
                    IntervalDistance(stem.Line.Top, stem.Line.Bottom, box.Top, box.Bottom) <= staff.Space * .45)
                .ToList();

            note.BeamCount = touching.Count;
            if (touching.Count == 0) continue;
            note.Type = touching.Count >= 2 ? "16th" : "eighth";
            note.Duration = DurationForType(note.Type, config.Divisions);
        }

        // Primary beam groups: one elongated beam touching at least two stems.
        foreach (var beam in beams)
        {
            var staff = ClosestStaff(beam.CenterX, beam.CenterY, staves, 6);
            if (staff is null) continue;

            var group = notes
                .Where(x => x.StaffIndex == staff.Index && x.StemX.HasValue && x.BeamCount > 0)
                .Where(x => x.StemX!.Value >= beam.Left - staff.Space * .25 && x.StemX.Value <= beam.Right + staff.Space * .25)
                .Where(x => stems.Any(s => s.StaffIndex == staff.Index &&
                    Math.Abs(s.Line.CenterX - x.StemX.Value) <= staff.Space * .12 &&
                    IntervalDistance(s.Line.Top, s.Line.Bottom, beam.Top, beam.Bottom) <= staff.Space * .45))
                .OrderBy(x => x.StemX)
                .ToList();

            var distinctStems = group.GroupBy(x => Math.Round(x.StemX!.Value / (staff.Space * .12))).ToList();
            if (distinctStems.Count < 2) continue;

            for (var i = 0; i < distinctStems.Count; i++)
            {
                var value = i == 0 ? "begin" : i == distinctStems.Count - 1 ? "end" : "continue";
                foreach (var note in distinctStems[i]) note.BeamValue = value;
            }
        }
    }

    private static List<Box> FindArcShapes(AnalysisResult analysis, IReadOnlyList<Box> beams)
    {
        var result = new List<Box>();
        foreach (var path in analysis.DirectPaths)
        {
            var box = Bounds(path);
            var staff = ClosestStaff(box.CenterX, box.CenterY, analysis.Staves, 5);
            if (staff is null) continue;
            var points = path.Geometry.Contours.Sum(x => x.Count);

            if (box.Width < staff.Space * 2.0 || box.Width > staff.Space * 18) continue;
            if (box.Height < staff.Space * .35 || box.Height > staff.Space * 2.6) continue;
            if (box.Width / Math.Max(box.Height, .001) < 2.0) continue;
            if (points < 16) continue;
            if (beams.Any(x => ReferenceEquals(x.Path, path))) continue;
            result.Add(box);
        }
        return result;
    }

    private static void AttachArcs(IReadOnlyList<RecognizedEvent> notes, IReadOnlyList<Box> arcs, IReadOnlyList<Staff> staves)
    {
        var slurNumber = 1;
        foreach (var arc in arcs.OrderBy(x => x.Left))
        {
            var staff = ClosestStaff(arc.CenterX, arc.CenterY, staves, 5);
            if (staff is null) continue;

            var staffNotes = notes.Where(x => x.StaffIndex == staff.Index).ToList();
            var start = staffNotes
                .Where(x => Math.Abs(x.X - arc.Left) <= staff.Space * 1.25)
                .OrderBy(x => Math.Abs(x.X - arc.Left))
                .ThenBy(x => Math.Abs(x.Y - arc.CenterY))
                .FirstOrDefault();
            var end = staffNotes
                .Where(x => Math.Abs(x.X - arc.Right) <= staff.Space * 1.25)
                .OrderBy(x => Math.Abs(x.X - arc.Right))
                .ThenBy(x => Math.Abs(x.Y - arc.CenterY))
                .FirstOrDefault();

            if (start is null || end is null || ReferenceEquals(start, end)) continue;

            if (start.Step == end.Step && start.Octave == end.Octave && start.Alter == end.Alter)
            {
                start.TieStart = true;
                end.TieStop = true;
            }
            else
            {
                start.SlurStart = true;
                start.SlurNumber = slurNumber;
                end.SlurStop = true;
                end.SlurNumber = slurNumber;
                slurNumber++;
            }
        }
    }

    private static Box Bounds(SvgDirectPath path)
    {
        var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
        return new Box(points.Min(x => x.X), points.Min(x => x.Y), points.Max(x => x.X), points.Max(x => x.Y), path);
    }

    private static Staff? ClosestStaff(double x, double y, IReadOnlyList<Staff> staves, double maxSpaces) => staves
        .Where(s => x >= s.Left - s.Space * 3 && x <= s.Right + s.Space * 3)
        .Select(s => new { Staff = s, Distance = Math.Abs(y - s.Center) / Math.Max(s.Space, .001) })
        .Where(x => x.Distance <= maxSpaces)
        .OrderBy(x => x.Distance)
        .Select(x => x.Staff)
        .FirstOrDefault();

    private static double IntervalDistance(double a1, double a2, double b1, double b2)
    {
        var aTop = Math.Min(a1, a2); var aBottom = Math.Max(a1, a2);
        var bTop = Math.Min(b1, b2); var bBottom = Math.Max(b1, b2);
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
