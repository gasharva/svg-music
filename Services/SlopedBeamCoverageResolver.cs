using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Completes long sloped beam groups after the primary sloped-beam detector has found the beam shape.
/// Beam membership is intentionally independent of StaffIndex: a rising/falling beamed passage can
/// cross the geometric boundary between staves while still belonging to one continuous beam.
/// SVG CSS classes are intentionally ignored.
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

        var averageSpace = analysis.Staves.Average(x => x.Space);

        foreach (var beam in FindBeamModels(analysis))
        {
            var blackNotes = analysis.Events
                .Where(x => x.Kind.Equals("notehead-black", StringComparison.OrdinalIgnoreCase))
                .Where(x => IsWithinBeamHorizontalRange(x, beam, averageSpace))
                .ToList();

            // First collect the strongest evidence: notes for which we can recover an actual stem
            // and one end of that stem reaches the fitted beam line.
            var verified = blackNotes
                .Where(x => x.StemX.HasValue)
                .Select(note => new
                {
                    Note = note,
                    Stem = FindStem(analysis, note, averageSpace)
                })
                .Where(x => x.Stem is not null)
                .Where(x => ReachesBeam(x.Note, x.Stem!, beam, averageSpace))
                .Select(x => x.Note)
                .ToList();

            // Some exporters/paths do not expose every visible stem as a usable LineSegment.
            // Do not let that truncate an otherwise obvious continuous beam. Once at least two
            // stem-confirmed members establish a beam group, use their head-to-beam geometry as
            // a local template and recover matching black noteheads directly.
            var members = RecoverByNoteheadGeometry(blackNotes, verified, beam, averageSpace)
                .OrderBy(EffectiveX)
                .ToList();

            var stemGroups = members
                .GroupBy(x => Math.Round(EffectiveX(x) / (averageSpace * .12)))
                .OrderBy(x => x.Average(EffectiveX))
                .ToList();

            if (stemGroups.Count < 2) continue;

            for (var i = 0; i < stemGroups.Count; i++)
            {
                var beamValue = i == 0 ? "begin" : i == stemGroups.Count - 1 ? "end" : "continue";
                foreach (var note in stemGroups[i])
                {
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

    private static bool IsWithinBeamHorizontalRange(RecognizedEvent note, BeamModel beam, double staffSpace)
    {
        var x = EffectiveX(note);

        // When StemX is known, the physical stem should be almost exactly inside the beam span.
        // For a note whose stem failed to parse, EffectiveX falls back to the notehead centre. A
        // down/up stem sits at the notehead edge, so the head centre can legitimately fall roughly
        // half a staff-space beyond the path endpoint. Give only those stemless candidates a wider
        // horizontal margin; vertical beam-profile matching below still decides membership.
        var margin = note.StemX.HasValue ? staffSpace * .35 : staffSpace * .95;
        return x >= beam.Left - margin && x <= beam.Right + margin;
    }

    private static IReadOnlyList<RecognizedEvent> RecoverByNoteheadGeometry(
        IReadOnlyList<RecognizedEvent> candidates,
        IReadOnlyList<RecognizedEvent> verified,
        BeamModel beam,
        double staffSpace)
    {
        if (verified.Count < 2)
            return verified;

        var verifiedSet = verified.ToHashSet();
        var samples = verified
            .Select(note => new
            {
                X = EffectiveX(note),
                Offset = note.Y - beam.YAt(Math.Clamp(EffectiveX(note), beam.Left, beam.Right))
            })
            .ToArray();
        var offsets = samples.Select(x => x.Offset).ToArray();

        // A real beam group has all noteheads on the same side of the beam. Median is robust to
        // one imperfectly attached head/stem and also tells us whether the beam is above or below.
        var orderedOffsets = offsets.OrderBy(x => x).ToArray();
        var medianOffset = orderedOffsets[orderedOffsets.Length / 2];
        var side = Math.Sign(medianOffset);
        if (side == 0) side = Math.Sign(offsets.Average());
        if (side == 0) return verified;

        // Head-to-beam distance is not necessarily constant in a rising/falling passage. The last
        // stem can be much shorter than the preceding stems, which made the old min/max envelope
        // reject the tail note in Yellow Leaves measure 4. Fit the local offset trend instead and
        // accept missing-stem heads that stay near that extrapolated engraving profile.
        var meanX = samples.Average(x => x.X);
        var meanOffset = samples.Average(x => x.Offset);
        var denominator = samples.Sum(x => (x.X - meanX) * (x.X - meanX));
        var offsetSlope = denominator <= .001
            ? 0
            : samples.Sum(x => (x.X - meanX) * (x.Offset - meanOffset)) / denominator;
        var offsetIntercept = meanOffset - offsetSlope * meanX;

        var verifiedResidual = samples
            .Select(x => Math.Abs(x.Offset - (offsetSlope * x.X + offsetIntercept)))
            .DefaultIfEmpty(0)
            .Max();
        var residualTolerance = Math.Min(
            staffSpace * 2.5,
            Math.Max(staffSpace * 1.35, verifiedResidual + staffSpace * 1.15));
        var maxDistance = Math.Min(
            staffSpace * 6.5,
            offsets.Select(Math.Abs).Max() + staffSpace * 2.0);

        var inferredDirection = verified
            .Where(x => x.StemDirection is "up" or "down")
            .GroupBy(x => x.StemDirection)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .FirstOrDefault() ?? (side < 0 ? "down" : "up");

        var result = new List<RecognizedEvent>(verified);
        foreach (var note in candidates)
        {
            if (verifiedSet.Contains(note)) continue;

            var x = EffectiveX(note);
            var beamX = Math.Clamp(x, beam.Left, beam.Right);
            var offset = note.Y - beam.YAt(beamX);
            var noteSide = Math.Sign(offset);
            if (noteSide != side) continue;
            if (Math.Abs(offset) > maxDistance) continue;

            var expectedOffset = offsetSlope * x + offsetIntercept;
            if (Math.Abs(offset - expectedOffset) > residualTolerance) continue;

            if (note.StemDirection is not ("up" or "down"))
                note.StemDirection = inferredDirection;

            result.Add(note);
        }

        return result;
    }

    private static double EffectiveX(RecognizedEvent note) => note.StemX ?? note.X;

    private static bool ReachesBeam(
        RecognizedEvent note,
        SvgLineSegment stem,
        BeamModel beam,
        double staffSpace)
    {
        var x = note.StemX!.Value;
        var expectedY = beam.YAt(Math.Clamp(x, beam.Left, beam.Right));

        // Prefer the known musical stem direction, but accept whichever end physically reaches
        // the beam if the provisional direction/staff assignment was wrong. This is important for
        // passages that travel between the two staves of a grand staff.
        var preferredEndY = note.StemDirection switch
        {
            "up" => stem.Top,
            "down" => stem.Bottom,
            _ => Math.Abs(stem.Top - expectedY) <= Math.Abs(stem.Bottom - expectedY) ? stem.Top : stem.Bottom
        };
        var nearestEndY = Math.Abs(stem.Top - expectedY) <= Math.Abs(stem.Bottom - expectedY)
            ? stem.Top
            : stem.Bottom;

        var tolerance = Math.Max(staffSpace * .75, beam.Thickness * 2.0);
        return Math.Abs(preferredEndY - expectedY) <= tolerance ||
               Math.Abs(nearestEndY - expectedY) <= tolerance;
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

    private static SvgLineSegment? FindStem(
        AnalysisResult analysis,
        RecognizedEvent note,
        double staffSpace) =>
        analysis.LineSegments
            .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staffSpace * .16)
            .Where(x => x.Height >= staffSpace * 1.15 && x.Height <= staffSpace * 8.0)
            .Where(x => x.Top <= note.Y + staffSpace * .85 && x.Bottom >= note.Y - staffSpace * .85)
            .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
            .ThenBy(x => Math.Abs(x.CenterY - note.Y))
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
