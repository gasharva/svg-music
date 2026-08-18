using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds curved filled strips that terminate near recognized note heads or stems.
/// Unlike beams, arcs are identified by curvature and endpoint proximity rather than physical contact.
/// Arcs are deliberately allowed to be PhysicalOnly because slurs/ties often cross barlines and
/// therefore cannot always belong to one P+M or measure scope.
/// </summary>
public sealed class ArcResolver
{
    public double MinWidthInStaffSpaces { get; init; } = 0.90;
    public double MinCurvatureInStaffSpaces { get; init; } = 0.16;
    public double MaxEndpointThicknessInStaffSpaces { get; init; } = 0.55;
    public double EndpointBandFraction { get; init; } = 0.12;
    public double MidBandFraction { get; init; } = 0.12;
    public double EndpointContactDistanceInStaffSpaces { get; init; } = 2.0;

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
            var contactDistance = staffSpace * EndpointContactDistanceInStaffSpaces;

            var leftContacts = FindEndpointContacts(candidate.LeftEndpoint, contactDistance, noteHeads, stems);
            var rightContacts = FindEndpointContacts(candidate.RightEndpoint, contactDistance, noteHeads, stems);

            var leftNearest = FindNearestDistance(candidate.LeftEndpoint, noteHeads, stems);
            var rightNearest = FindNearestDistance(candidate.RightEndpoint, noteHeads, stems);

            if (leftContacts.Count == 0 || rightContacts.Count == 0)
            {
                diagnostics.Add(analysis.Diagnostic with
                {
                    Stage = "contacts",
                    Verdict = leftContacts.Count == 0 && rightContacts.Count == 0
                        ? "rejected: no endpoint contacts on either side"
                        : leftContacts.Count == 0
                            ? "rejected: no left endpoint contact"
                            : "rejected: no right endpoint contact",
                    LeftNearestContactDistanceInStaffSpaces = leftNearest / staffSpace,
                    RightNearestContactDistanceInStaffSpaces = rightNearest / staffSpace,
                    LeftContactCount = leftContacts.Count,
                    RightContactCount = rightContacts.Count
                });
                continue;
            }

            var notes = leftContacts
                .Concat(rightContacts)
                .Select(x => x.Note)
                .Where(x => x is not null)
                .Cast<NoteHeadResolution>()
                .Distinct()
                .ToArray();

            var contactedStems = leftContacts
                .Concat(rightContacts)
                .Select(x => x.Stem)
                .Where(x => x is not null)
                .Cast<StemResolution>()
                .Distinct()
                .ToArray();

            if (notes.Length + contactedStems.Length == 0)
            {
                diagnostics.Add(analysis.Diagnostic with
                {
                    Stage = "contacts",
                    Verdict = "rejected: endpoint contacts resolved to no notes/stems",
                    LeftNearestContactDistanceInStaffSpaces = leftNearest / staffSpace,
                    RightNearestContactDistanceInStaffSpaces = rightNearest / staffSpace,
                    LeftContactCount = leftContacts.Count,
                    RightContactCount = rightContacts.Count
                });
                continue;
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
                Verdict = $"accepted: notes={notes.Length}, stems={contactedStems.Length}",
                LeftNearestContactDistanceInStaffSpaces = leftNearest / staffSpace,
                RightNearestContactDistanceInStaffSpaces = rightNearest / staffSpace,
                LeftContactCount = leftContacts.Count,
                RightContactCount = rightContacts.Count,
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

        if (widthInStaffSpaces < MinWidthInStaffSpaces)
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

        if (leftThicknessInStaffSpaces > MaxEndpointThicknessInStaffSpaces ||
            rightThicknessInStaffSpaces > MaxEndpointThicknessInStaffSpaces)
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

        if (curvatureInStaffSpaces < MinCurvatureInStaffSpaces)
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

        if (thicknessRatio > 3.0)
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
            "geometry accepted; checking endpoint contacts",
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

    private static IReadOnlyList<EndpointContact> FindEndpointContacts(
        PointD endpoint,
        double maxDistance,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<StemResolution> stems)
    {
        var contacts = new List<EndpointContact>();

        foreach (var note in noteHeads)
        {
            var distance = DistanceToRect(endpoint, note.PhysicalBounds);
            if (distance <= maxDistance)
                contacts.Add(new EndpointContact(note, null, distance));
        }

        foreach (var stem in stems)
        {
            var distance = DistanceToRect(endpoint, stem.PhysicalBounds);
            if (distance <= maxDistance)
                contacts.Add(new EndpointContact(null, stem, distance));
        }

        if (contacts.Count == 0)
            return Array.Empty<EndpointContact>();

        var nearest = contacts.Min(x => x.Distance);
        var keepWithin = Math.Max(0.01, maxDistance * 0.18);

        var localContacts = contacts
            .Where(x => x.Distance <= nearest + keepWithin)
            .OrderBy(x => x.Distance)
            .ToArray();

        return localContacts;
    }

    private static double FindNearestDistance(
        PointD endpoint,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<StemResolution> stems)
    {
        var nearest = double.PositiveInfinity;

        foreach (var note in noteHeads)
            nearest = Math.Min(nearest, DistanceToRect(endpoint, note.PhysicalBounds));

        foreach (var stem in stems)
            nearest = Math.Min(nearest, DistanceToRect(endpoint, stem.PhysicalBounds));

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

    private sealed record ArcCandidate(PointD LeftEndpoint, PointD Midpoint, PointD RightEndpoint);
    private sealed record ArcCandidateAnalysis(ArcCandidate? Candidate, ArcDiagnosticEntry Diagnostic);
    private sealed record EndpointContact(NoteHeadResolution? Note, StemResolution? Stem, double Distance);
}
