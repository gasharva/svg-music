using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Rebuilds curved-line semantics after pitch/chord reconstruction.
/// A curve between equal pitches is a tie only when the curve endpoints also terminate near those
/// noteheads; a curve between different pitches (or a high arch anchored at stems) is a slur.
/// Compound PDF-derived paths are decomposed contour-by-contour because one SVG path can contain
/// several unrelated slurs across neighbouring measures.
/// </summary>
public sealed class ArcSemanticsResolver
{
    private sealed record Arc(
        double Left,
        double Right,
        double Top,
        double Bottom,
        double LeftY,
        double RightY,
        string SourceId)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;
        public double CenterX => (Left + Right) / 2;
        public double CenterY => (Top + Bottom) / 2;
    }

    private sealed record PairCandidate(
        RecognizedEvent Start,
        RecognizedEvent End,
        double GeometryScore,
        bool TieCompatible);

    public void Resolve(AnalysisResult analysis)
    {
        if (analysis.Staves.Count == 0) return;

        var notes = analysis.Events
            .Where(x => x.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Step is not null && x.StaffIndex >= 0)
            .ToList();
        if (notes.Count == 0) return;

        foreach (var note in notes)
        {
            note.TieStart = false;
            note.TieStop = false;
            note.SlurStart = false;
            note.SlurStop = false;
            note.SlurNumber = null;
        }

        var beamContours = FindBeamContours(analysis);
        var arcs = FindArcs(analysis, beamContours);
        var usedPairs = new HashSet<(RecognizedEvent Start, RecognizedEvent End)>();
        var slurNumber = 1;

        foreach (var arc in arcs.OrderBy(x => x.Left).ThenBy(x => x.CenterY))
        {
            var staff = ClosestStaff(arc.CenterX, arc.CenterY, analysis.Staves, 5);
            if (staff is null) continue;

            var staffNotes = notes.Where(x => x.StaffIndex == staff.Index).ToList();
            var starts = staffNotes
                .Where(x => Math.Abs(x.X - arc.Left) <= staff.Space * 2.25)
                .ToList();
            var ends = staffNotes
                .Where(x => Math.Abs(x.X - arc.Right) <= staff.Space * 2.25)
                .ToList();

            var best = starts
                .SelectMany(start => ends
                    .Where(end => !ReferenceEquals(start, end))
                    .Select(end => BuildCandidate(start, end, arc, staff.Space)))
                .Where(x => !usedPairs.Contains((x.Start, x.End)))
                .OrderBy(x => x.GeometryScore - (x.TieCompatible ? 1.65 : 0))
                .ThenBy(x => x.GeometryScore)
                .FirstOrDefault();

            if (best is null || best.GeometryScore > 10.0) continue;

            usedPairs.Add((best.Start, best.End));
            if (best.TieCompatible)
            {
                best.Start.TieStart = true;
                best.End.TieStop = true;
            }
            else
            {
                best.Start.SlurStart = true;
                best.Start.SlurNumber = slurNumber;
                best.End.SlurStop = true;
                best.End.SlurNumber = slurNumber;
                slurNumber++;
            }
        }
    }

    private static PairCandidate BuildCandidate(
        RecognizedEvent start,
        RecognizedEvent end,
        Arc arc,
        double staffSpace)
    {
        var startScore = Math.Abs(start.X - arc.Left) / Math.Max(staffSpace, .001) +
                         .9 * Math.Abs(start.Y - arc.LeftY) / Math.Max(staffSpace, .001);
        var endScore = Math.Abs(end.X - arc.Right) / Math.Max(staffSpace, .001) +
                       .9 * Math.Abs(end.Y - arc.RightY) / Math.Max(staffSpace, .001);

        return new PairCandidate(start, end, startScore + endScore, CanTie(start, end, arc, staffSpace));
    }

    private static bool CanTie(
        RecognizedEvent start,
        RecognizedEvent end,
        Arc arc,
        double staffSpace)
    {
        if (!string.Equals(start.Step, end.Step, StringComparison.Ordinal) || start.Octave != end.Octave)
            return false;

        var maxEndpointYOffset = staffSpace * 1.25;
        if (Math.Abs(start.Y - arc.LeftY) > maxEndpointYOffset ||
            Math.Abs(end.Y - arc.RightY) > maxEndpointYOffset)
            return false;

        if (start.Alter == end.Alter) return true;
        return string.IsNullOrWhiteSpace(end.AttachedToSymbolId);
    }

    /// <summary>
    /// Identify beam contours, not whole SVG paths. A PDF exporter often stores the primary beam
    /// and a short hook as sibling contours in one path. Conversely, another compound path can hold
    /// several slurs. Whole-path exclusion therefore destroys real arcs.
    /// </summary>
    private static HashSet<(string PathId, int ContourIndex)> FindBeamContours(AnalysisResult analysis)
    {
        var result = new HashSet<(string, int)>();

        foreach (var path in analysis.DirectPaths)
        for (var contourIndex = 0; contourIndex < path.Geometry.Contours.Count; contourIndex++)
        {
            var contour = path.Geometry.Contours[contourIndex];
            if (contour.Count is < 3 or > 14) continue;

            var left = contour.Min(x => x.X);
            var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y);
            var bottom = contour.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;
            var staff = ClosestStaff((left + right) / 2, (top + bottom) / 2, analysis.Staves, 6);
            if (staff is null) continue;

            if (width < staff.Space * 1.4 || width > staff.Space * 22) continue;
            if (height < staff.Space * .06 || height > staff.Space * 2.0) continue;
            if (width / Math.Max(height, .001) < 1.4) continue;

            // Beam strips are materially thicker than the tapered outline of a slur. This keeps
            // compact 8-point polygonized slurs out of the beam exclusion set.
            var area = PolygonArea(contour);
            var longAxis = Math.Sqrt(width * width + height * height);
            var thickness = area / Math.Max(longAxis, .001);
            if (thickness < staff.Space * .20 || thickness > staff.Space * .72) continue;

            result.Add((path.SymbolId, contourIndex));
        }

        return result;
    }

    private static List<Arc> FindArcs(
        AnalysisResult analysis,
        IReadOnlySet<(string PathId, int ContourIndex)> beamContours)
    {
        var result = new List<Arc>();

        foreach (var path in analysis.DirectPaths)
        for (var contourIndex = 0; contourIndex < path.Geometry.Contours.Count; contourIndex++)
        {
            if (beamContours.Contains((path.SymbolId, contourIndex))) continue;

            var contour = path.Geometry.Contours[contourIndex];
            if (contour.Count == 0) continue;

            var points = contour.ToArray();
            var left = points.Min(x => x.X);
            var right = points.Max(x => x.X);
            var top = points.Min(x => x.Y);
            var bottom = points.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;
            var staff = ClosestStaff((left + right) / 2, (top + bottom) / 2, analysis.Staves, 5);
            if (staff is null) continue;

            if (width < staff.Space * 2.0 || width > staff.Space * 18) continue;
            if (height < staff.Space * .35 || height > staff.Space * 4.8) continue;
            if (width / Math.Max(height, .001) < 2.0) continue;

            // Direct PDF outlines can be compact 8-point polygons; reusable glyph curves normally
            // contain many more flattened points. Apply the threshold to each contour independently.
            var minPoints = path.SymbolId.StartsWith("path:", StringComparison.Ordinal) ? 8 : 16;
            if (points.Length < minPoints) continue;

            var leftY = EndpointY(points, left, staff.Space);
            var rightY = EndpointY(points, right, staff.Space);
            result.Add(new Arc(left, right, top, bottom, leftY, rightY, $"{path.SymbolId}#{contourIndex}"));
        }

        return result;
    }

    private static double EndpointY(IReadOnlyList<PointD> points, double endpointX, double staffSpace)
    {
        var tolerance = Math.Max(.01, staffSpace * .02);
        var endpointPoints = points.Where(x => Math.Abs(x.X - endpointX) <= tolerance).ToList();
        return endpointPoints.Count > 0
            ? endpointPoints.Average(x => x.Y)
            : points.OrderBy(x => Math.Abs(x.X - endpointX)).First().Y;
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

    private static Staff? ClosestStaff(
        double x,
        double y,
        IReadOnlyList<Staff> staves,
        double maxSpaces) => staves
        .Where(s => x >= s.Left - s.Space * 3 && x <= s.Right + s.Space * 3)
        .Select(s => new { Staff = s, Distance = Math.Abs(y - s.Center) / Math.Max(s.Space, .001) })
        .Where(x => x.Distance <= maxSpaces)
        .OrderBy(x => x.Distance)
        .Select(x => x.Staff)
        .FirstOrDefault();
}
