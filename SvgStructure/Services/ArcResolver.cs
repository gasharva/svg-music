using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds curved filled strips whose endpoints form one of two supported musical attachment patterns:
/// note head -> different note head, or stem end -> different stem end.
/// Shape measurements are currently diagnostic only; attachment semantics are the primary filter.
/// </summary>
public sealed class ArcResolver
{
    public double MinWidthInStaffSpaces { get; init; } = 0.90;
    public double MinCurvatureInStaffSpaces { get; init; } = 0.16;
    public double MaxEndpointThicknessInStaffSpaces { get; init; } = 0.55;
    public double EndpointBandFraction { get; init; } = 0.12;
    public double MidBandFraction { get; init; } = 0.12;

    /// <summary>Maximum distance from an arc endpoint to its attached note head or stem end.</summary>
    public double EndpointContactDistanceInStaffSpaces { get; init; } = 2.0;

    /// <summary>Minimum straight-line distance between the two arc endpoints, measured in staff spaces.</summary>
    public double MinArcLengthInStaffSpaces { get; init; } = 2.0;

    // These shape thresholds were heuristic guesses. Keep their measurements in diagnostics,
    // but disable rejection by them while we inspect real score data.
    public bool FilterByMinimumWidth { get; init; } = false;
    public bool FilterByEndpointThickness { get; init; } = false;
    public bool FilterByMinimumCurvature { get; init; } = false;
    public bool FilterByEndpointThicknessSymmetry { get; init; } = false;

    public IReadOnlyList<ArcDiagnosticEntry> LastDiagnostics { get; private set; } = Array.Empty<ArcDiagnosticEntry>();

    public IReadOnlyList<ArcResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<StemResolution> stems)
    {
        var result = new List<ArcResolution>();
        var diagnostics = new List<ArcDiagnosticEntry>();

        foreach (var primitive in primitives.Primitives)
        {
            var staffSpace = StaffSpaceFor(primitive, grid);
            if (staffSpace <= 1e-9)
            {
                diagnostics.Add(new ArcDiagnosticEntry(
                    primitive.Id,
                    primitive.PhysicalBounds,
                    primitive.Contour.Points.Count,
                    staffSpace,
                    "staff-space",
                    "rejected: no usable staff-space"));
                continue;
            }

            var analysis = AnalyzeArcCandidate(
                primitive.Id,
                primitive.Contour,
                primitive.PhysicalBounds,
                staffSpace);
            diagnostics.Add(analysis.Diagnostic);

            if (analysis.Candidate is null)
                continue;

            var candidate = analysis.Candidate;
            var arcLengthInStaffSpaces = Distance(candidate.LeftEndpoint, candidate.RightEndpoint) / staffSpace;
            if (arcLengthInStaffSpaces < MinArcLengthInStaffSpaces)
            {
                diagnostics.Add(analysis.Diagnostic with
                {
                    Stage = "length",
                    Verdict = $"rejected: arc length {arcLengthInStaffSpaces:F2} < {MinArcLengthInStaffSpaces:F2} staff spaces"
                });
                continue;
            }

            var contactDistance = staffSpace * EndpointContactDistanceInStaffSpaces;

            var leftNoteContacts = FindNoteContacts(candidate.LeftEndpoint, contactDistance, noteHeads);
            var rightNoteContacts = FindNoteContacts(candidate.RightEndpoint, contactDistance, noteHeads);
            var leftStemContacts = FindStemEndContacts(candidate.LeftEndpoint, contactDistance, stems);
            var rightStemContacts = FindStemEndContacts(candidate.RightEndpoint, contactDistance, stems);

            var leftNearest = FindNearestAttachmentDistance(candidate.LeftEndpoint, noteHeads, stems);
            var rightNearest = FindNearestAttachmentDistance(candidate.RightEndpoint, noteHeads, stems);

            var notePair = FindDifferentNotePair(leftNoteContacts, rightNoteContacts);
            var stemPair = FindDifferentStemPair(leftStemContacts, rightStemContacts);

            if (notePair is null && stemPair is null)
            {
                diagnostics.Add(analysis.Diagnostic with
                {
                    Stage = "contacts",
                    Verdict = BuildAttachmentRejectionVerdict(
                        leftNoteContacts,
                        rightNoteContacts,
                        leftStemContacts,
                        rightStemContacts),
                    LeftNearestContactDistanceInStaffSpaces = leftNearest / staffSpace,
                    RightNearestContactDistanceInStaffSpaces = rightNearest / staffSpace,
                    LeftContactCount = leftNoteContacts.Count + leftStemContacts.Count,
                    RightContactCount = rightNoteContacts.Count + rightStemContacts.Count
                });
                continue;
            }

            IReadOnlyList<NoteHeadResolution> notes;
            IReadOnlyList<StemResolution> contactedStems;
            string acceptedKind;

            if (notePair is not null)
            {
                notes = new[] { notePair.Left, notePair.Right };
                contactedStems = Array.Empty<StemResolution>();
                acceptedKind = "note-note";
            }
            else
            {
                notes = Array.Empty<NoteHeadResolution>();
                contactedStems = new[] { stemPair!.Left, stemPair.Right };
                acceptedKind = "stem-end/stem-end";
            }

            result.Add(new ArcResolution(
                primitive.PhysicalBounds,
                candidate.LeftEndpoint,
                candidate.Midpoint,
                candidate.RightEndpoint,
                notes,
                contactedStems));

            diagnostics.Add(analysis.Diagnostic with
            {
                Stage = "accepted",
                Verdict = $"accepted: {acceptedKind}; length={arcLengthInStaffSpaces:F2} staff spaces",
                LeftNearestContactDistanceInStaffSpaces = leftNearest / staffSpace,
                RightNearestContactDistanceInStaffSpaces = rightNearest / staffSpace,
                LeftContactCount = leftNoteContacts.Count + leftStemContacts.Count,
                RightContactCount = rightNoteContacts.Count + rightStemContacts.Count,
                Accepted = true
            });
        }

        LastDiagnostics = diagnostics;

        return result
            .GroupBy(x => (
                LeftX: Math.Round(x.LeftEndpoint.X, 1),
                LeftY: Math.Round(x.LeftEndpoint.Y, 1),
                RightX: Math.Round(x.RightEndpoint.X, 1),
                RightY: Math.Round(x.RightEndpoint.Y, 1)))
            .Select(x => x.OrderByDescending(y => y.PhysicalBounds.Width).First())
            .OrderBy(x => x.PhysicalBounds.Top)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ToArray();
    }

    private ArcCandidateAnalysis AnalyzeArcCandidate(
        int primitiveId,
        PrimitiveContour contour,
        RectD bounds,
        double staffSpace)
    {
        var widthInStaffSpaces = bounds.Width / staffSpace;

        // Keep only the minimum structural requirements needed to compute endpoints/midpoint.
        if (contour.Points.Count < 6 || bounds.Width <= 1e-9 || bounds.Height <= 1e-9)
        {
            return Reject(
                primitiveId,
                bounds,
                contour.Points.Count,
                staffSpace,
                "shape",
                "rejected: too few contour points or empty bounds",
                widthInStaffSpaces);
        }

        if (FilterByMinimumWidth && widthInStaffSpaces < MinWidthInStaffSpaces)
        {
            return Reject(
                primitiveId,
                bounds,
                contour.Points.Count,
                staffSpace,
                "width",
                $"rejected: width {widthInStaffSpaces:F2} < {MinWidthInStaffSpaces:F2}",
                widthInStaffSpaces);
        }

        var endpointBandWidth = Math.Max(bounds.Width * EndpointBandFraction, staffSpace * 0.06);
        var midBandHalfWidth = Math.Max(bounds.Width * MidBandFraction / 2.0, staffSpace * 0.05);
        var centerX = bounds.CenterX;

        var leftPoints = contour.Points
            .Where(p => p.X <= bounds.Left + endpointBandWidth)
            .ToArray();
        var rightPoints = contour.Points
            .Where(p => p.X >= bounds.Right - endpointBandWidth)
            .ToArray();
        var midPoints = contour.Points
            .Where(p => Math.Abs(p.X - centerX) <= midBandHalfWidth)
            .ToArray();

        if (leftPoints.Length < 2 || rightPoints.Length < 2 || midPoints.Length < 2)
        {
            return Reject(
                primitiveId,
                bounds,
                contour.Points.Count,
                staffSpace,
                "bands",
                $"rejected: insufficient band points L={leftPoints.Length}, M={midPoints.Length}, R={rightPoints.Length}",
                widthInStaffSpaces);
        }

        var leftMinY = leftPoints.Min(p => (double)p.Y);
        var leftMaxY = leftPoints.Max(p => (double)p.Y);
        var rightMinY = rightPoints.Min(p => (double)p.Y);
        var rightMaxY = rightPoints.Max(p => (double)p.Y);
        var midMinY = midPoints.Min(p => (double)p.Y);
        var midMaxY = midPoints.Max(p => (double)p.Y);

        var leftThicknessInStaffSpaces = (leftMaxY - leftMinY) / staffSpace;
        var rightThicknessInStaffSpaces = (rightMaxY - rightMinY) / staffSpace;

        if (FilterByEndpointThickness &&
            (leftThicknessInStaffSpaces > MaxEndpointThicknessInStaffSpaces ||
             rightThicknessInStaffSpaces > MaxEndpointThicknessInStaffSpaces))
        {
            return Reject(
                primitiveId,
                bounds,
                contour.Points.Count,
                staffSpace,
                "thickness",
                $"rejected: endpoint thickness L={leftThicknessInStaffSpaces:F2}, R={rightThicknessInStaffSpaces:F2} > {MaxEndpointThicknessInStaffSpaces:F2}",
                widthInStaffSpaces,
                leftThicknessInStaffSpaces,
                rightThicknessInStaffSpaces);
        }

        var left = new PointD(bounds.Left, (leftMinY + leftMaxY) / 2.0);
        var right = new PointD(bounds.Right, (rightMinY + rightMaxY) / 2.0);
        var middle = new PointD(centerX, (midMinY + midMaxY) / 2.0);

        var straightMidY = (left.Y + right.Y) / 2.0;
        var curvatureInStaffSpaces = Math.Abs(middle.Y - straightMidY) / staffSpace;

        if (FilterByMinimumCurvature && curvatureInStaffSpaces < MinCurvatureInStaffSpaces)
        {
            return Reject(
                primitiveId,
                bounds,
                contour.Points.Count,
                staffSpace,
                "curvature",
                $"rejected: curvature {curvatureInStaffSpaces:F2} < {MinCurvatureInStaffSpaces:F2}",
                widthInStaffSpaces,
                leftThicknessInStaffSpaces,
                rightThicknessInStaffSpaces,
                curvatureInStaffSpaces,
                left,
                middle,
                right);
        }

        var minThickness = Math.Max(1e-9, Math.Min(leftMaxY - leftMinY, rightMaxY - rightMinY));
        var maxThickness = Math.Max(leftMaxY - leftMinY, rightMaxY - rightMinY);
        var thicknessRatio = maxThickness / minThickness;

        if (FilterByEndpointThicknessSymmetry && thicknessRatio > 3.0)
        {
            return Reject(
                primitiveId,
                bounds,
                contour.Points.Count,
                staffSpace,
                "symmetry",
                $"rejected: endpoint thickness ratio {thicknessRatio:F2} > 3.00",
                widthInStaffSpaces,
                leftThicknessInStaffSpaces,
                rightThicknessInStaffSpaces,
                curvatureInStaffSpaces,
                left,
                middle,
                right);
        }

        var diagnostic = new ArcDiagnosticEntry(
            primitiveId,
            bounds,
            contour.Points.Count,
            staffSpace,
            "geometry",
            "geometry measured; checking minimum length and strict note-note / stem-end-stem-end attachment",
            widthInStaffSpaces,
            leftThicknessInStaffSpaces,
            rightThicknessInStaffSpaces,
            curvatureInStaffSpaces,
            left,
            middle,
            right);

        return new ArcCandidateAnalysis(new ArcCandidate(left, middle, right), diagnostic);
    }

    private static ArcCandidateAnalysis Reject(
        int primitiveId,
        RectD bounds,
        int contourPointCount,
        double staffSpace,
        string stage,
        string verdict,
        double? widthInStaffSpaces = null,
        double? leftThicknessInStaffSpaces = null,
        double? rightThicknessInStaffSpaces = null,
        double? curvatureInStaffSpaces = null,
        PointD? left = null,
        PointD? middle = null,
        PointD? right = null)
    {
        var diagnostic = new ArcDiagnosticEntry(
            primitiveId,
            bounds,
            contourPointCount,
            staffSpace,
            stage,
            verdict,
            widthInStaffSpaces,
            leftThicknessInStaffSpaces,
            rightThicknessInStaffSpaces,
            curvatureInStaffSpaces,
            left,
            middle,
            right);

        return new ArcCandidateAnalysis(null, diagnostic);
    }

    private static IReadOnlyList<NoteContact> FindNoteContacts(
        PointD endpoint,
        double maxDistance,
        IReadOnlyList<NoteHeadResolution> noteHeads)
    {
        return noteHeads
            .Select(note => new NoteContact(note, DistanceToRect(endpoint, note.PhysicalBounds)))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .ToArray();
    }

    private static IReadOnlyList<StemEndContact> FindStemEndContacts(
        PointD endpoint,
        double maxDistance,
        IReadOnlyList<StemResolution> stems)
    {
        var contacts = new List<StemEndContact>();

        foreach (var stem in stems)
        {
            var top = new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Top);
            var bottom = new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Bottom);
            var topDistance = Distance(endpoint, top);
            var bottomDistance = Distance(endpoint, bottom);
            var distance = Math.Min(topDistance, bottomDistance);

            if (distance <= maxDistance)
                contacts.Add(new StemEndContact(stem, distance));
        }

        return contacts
            .OrderBy(x => x.Distance)
            .ToArray();
    }

    private static NotePair? FindDifferentNotePair(
        IReadOnlyList<NoteContact> left,
        IReadOnlyList<NoteContact> right)
    {
        foreach (var leftContact in left)
        foreach (var rightContact in right)
        {
            if (!ReferenceEquals(leftContact.Note, rightContact.Note))
                return new NotePair(leftContact.Note, rightContact.Note);
        }

        return null;
    }

    private static StemPair? FindDifferentStemPair(
        IReadOnlyList<StemEndContact> left,
        IReadOnlyList<StemEndContact> right)
    {
        foreach (var leftContact in left)
        foreach (var rightContact in right)
        {
            if (!ReferenceEquals(leftContact.Stem, rightContact.Stem))
                return new StemPair(leftContact.Stem, rightContact.Stem);
        }

        return null;
    }

    private static string BuildAttachmentRejectionVerdict(
        IReadOnlyList<NoteContact> leftNotes,
        IReadOnlyList<NoteContact> rightNotes,
        IReadOnlyList<StemEndContact> leftStems,
        IReadOnlyList<StemEndContact> rightStems)
    {
        if (leftNotes.Count > 0 && rightNotes.Count > 0)
            return "rejected: both ends reach notes, but not two different notes";

        if (leftStems.Count > 0 && rightStems.Count > 0)
            return "rejected: both ends reach stem ends, but not two different stems";

        if ((leftNotes.Count > 0 && rightStems.Count > 0) ||
            (leftStems.Count > 0 && rightNotes.Count > 0))
            return "rejected: mixed note/stem attachment is not allowed";

        return "rejected: endpoints do not form note-note or stem-end/stem-end pair";
    }

    private static double FindNearestAttachmentDistance(
        PointD endpoint,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<StemResolution> stems)
    {
        var nearest = double.PositiveInfinity;

        foreach (var note in noteHeads)
            nearest = Math.Min(nearest, DistanceToRect(endpoint, note.PhysicalBounds));

        foreach (var stem in stems)
        {
            var top = new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Top);
            var bottom = new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Bottom);
            nearest = Math.Min(nearest, Math.Min(Distance(endpoint, top), Distance(endpoint, bottom)));
        }

        return nearest;
    }

    private static double StaffSpaceFor(ResolvedPrimitive primitive, LogicalGridResolution grid)
    {
        if (primitive.PartNumber is { } part &&
            primitive.MeasureNumber is { } measure &&
            grid.TryGetBlock(part, measure, out var ownBlock))
            return ownBlock.PhysicalBounds.Height / 4.0;

        if (primitive.MeasureNumber is not { } measureNumber)
            return grid.Blocks.Count == 0
                ? 0
                : grid.Blocks.Average(x => x.PhysicalBounds.Height / 4.0);

        var blocks = grid.Blocks
            .Where(x => x.MeasureNumber == measureNumber)
            .ToArray();

        return blocks.Length == 0
            ? 0
            : blocks.Average(x => x.PhysicalBounds.Height / 4.0);
    }

    private static double DistanceToRect(PointD point, RectD rect)
    {
        var dx = point.X < rect.Left
            ? rect.Left - point.X
            : point.X > rect.Right
                ? point.X - rect.Right
                : 0.0;

        var dy = point.Y < rect.Top
            ? rect.Top - point.Y
            : point.Y > rect.Bottom
                ? point.Y - rect.Bottom
                : 0.0;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Distance(PointD a, PointD b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed record ArcCandidate(PointD LeftEndpoint, PointD Midpoint, PointD RightEndpoint);
    private sealed record ArcCandidateAnalysis(ArcCandidate? Candidate, ArcDiagnosticEntry Diagnostic);
    private sealed record NoteContact(NoteHeadResolution Note, double Distance);
    private sealed record StemEndContact(StemResolution Stem, double Distance);
    private sealed record NotePair(NoteHeadResolution Left, NoteHeadResolution Right);
    private sealed record StemPair(StemResolution Left, StemResolution Right);
}