using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Rebuilds curved-line semantics after pitch/chord reconstruction.
/// A curve between equal pitches is a tie only when the curve endpoints also terminate near those
/// noteheads; a curve between different pitches (or a high arch anchored at stems) is a slur.
/// Endpoint geometry is matched jointly so separate ties on chord tones do not collapse into
/// one cross-pitch slur. SVG CSS classes are intentionally ignored.
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
        SvgDirectPath Path)
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

        // MusicGeometryRelationResolver already provides a first-pass attachment. Rebuild only
        // arc semantics here with endpoint-aware chord matching.
        foreach (var note in notes)
        {
            note.TieStart = false;
            note.TieStop = false;
            note.SlurStart = false;
            note.SlurStop = false;
            note.SlurNumber = null;
        }

        var beams = FindBeamPaths(analysis);
        var arcs = FindArcs(analysis, beams);
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
                // Same-pitch preference is allowed only after CanTie has established that this is
                // a physically compact tie ending near both noteheads. A high slur whose endpoints
                // sit at stem tops must never turn into a tie merely because its outer notes match.
                .OrderBy(x => x.GeometryScore - (x.TieCompatible ? 1.65 : 0))
                .ThenBy(x => x.GeometryScore)
                .FirstOrDefault();

            if (best is null) continue;
            // Legato slurs can arch several staff spaces away from their noteheads, unlike compact
            // ties. Horizontal endpoint windows already keep candidates local, so allow the larger
            // vertical score while still rejecting clearly unrelated pairs.
            if (best.GeometryScore > 10.0) continue;

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

        // Musical identity is necessary but not sufficient. A tie physically starts/ends at the
        // noteheads it prolongs. In measure 5 the long legato arc runs from stem top to stem top,
        // about 3-4 staff spaces above the equal F4 noteheads; treating that as F4->F4 created a
        // false tie. Genuine chord ties in measure 6 terminate within about one staff space of
        // their A3/Bb3 heads. Keep a small exporter/layout allowance, but reject stem-top arches.
        var maxEndpointYOffset = staffSpace * 1.25;
        if (Math.Abs(start.Y - arc.LeftY) > maxEndpointYOffset ||
            Math.Abs(end.Y - arc.RightY) > maxEndpointYOffset)
            return false;

        if (start.Alter == end.Alter) return true;

        // Accidentals persist through the measure. At this geometry stage the following note can
        // still have Alter=0 even though it inherits the explicit accidental from the first note;
        // MusicXmlAccidentalStatePostProcessor applies that state later. An explicit conflicting
        // accidental on the destination, however, means these are genuinely different pitches.
        return string.IsNullOrWhiteSpace(end.AttachedToSymbolId);
    }

    private static HashSet<SvgDirectPath> FindBeamPaths(AnalysisResult analysis)
    {
        var result = new HashSet<SvgDirectPath>();
        foreach (var path in analysis.DirectPaths)
        {
            var bounds = Bounds(path);
            var staff = ClosestStaff(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2, analysis.Staves, 6);
            if (staff is null) continue;

            var points = path.Geometry.Contours.Sum(x => x.Count);
            if (bounds.Width < staff.Space * 1.4) continue;
            if (bounds.Height < staff.Space * .08 || bounds.Height > staff.Space * .95) continue;
            if (bounds.Width / Math.Max(bounds.Height, .001) < 2.2) continue;
            if (points > 14) continue;
            result.Add(path);
        }
        return result;
    }

    private static List<Arc> FindArcs(AnalysisResult analysis, IReadOnlySet<SvgDirectPath> beams)
    {
        var result = new List<Arc>();
        foreach (var path in analysis.DirectPaths)
        {
            if (beams.Contains(path)) continue;

            var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
            if (points.Length == 0) continue;

            var left = points.Min(x => x.X);
            var right = points.Max(x => x.X);
            var top = points.Min(x => x.Y);
            var bottom = points.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;
            var staff = ClosestStaff((left + right) / 2, (top + bottom) / 2, analysis.Staves, 5);
            if (staff is null) continue;

            if (width < staff.Space * 2.0 || width > staff.Space * 18) continue;
            // Ties are compact, but legato slurs can arch several staff-spaces above the notes.
            if (height < staff.Space * .35 || height > staff.Space * 4.8) continue;
            if (width / Math.Max(height, .001) < 2.0) continue;
            if (points.Length < 16) continue;

            var leftY = EndpointY(points, left, staff.Space);
            var rightY = EndpointY(points, right, staff.Space);
            result.Add(new Arc(left, right, top, bottom, leftY, rightY, path));
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

    private static (double Left, double Top, double Width, double Height) Bounds(SvgDirectPath path)
    {
        var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
        var left = points.Min(x => x.X);
        var right = points.Max(x => x.X);
        var top = points.Min(x => x.Y);
        var bottom = points.Max(x => x.Y);
        return (left, top, right - left, bottom - top);
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
