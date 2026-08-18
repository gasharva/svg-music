using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds small smooth oval note heads from P+M primitives, attaches them to the logical staff grid,
/// and derives written pitch from the nearest recognized clef to the left. No glyph/PCA recognition.
/// </summary>
public sealed class NoteHeadResolver
{
    public double MinHeightInStaffSpaces { get; init; } = 0.45;
    public double MaxHeightInStaffSpaces { get; init; } = 1.35;
    public double MinAspectRatio { get; init; } = 1.05;
    public double MaxAspectRatio { get; init; } = 2.40;
    public double MaxLogicalYCenterOffset { get; init; } = 0.40;
    public double MaxRadialVariation { get; init; } = 0.34;
    public double HollowInnerMinWidthRatio { get; init; } = 0.25;
    public double HollowInnerMaxWidthRatio { get; init; } = 0.90;

    public IReadOnlyList<NoteHeadDiagnosticEntry> LastDiagnostics { get; private set; } =
        Array.Empty<NoteHeadDiagnosticEntry>();

    public IReadOnlyList<NoteHeadResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<LedgerLineResolution> ledgerLines)
    {
        var ovalCandidates = new List<OvalCandidate>();
        foreach (var primitive in primitives.Primitives)
        {
            if (primitive.Scope != PrimitiveLogicalScope.PartMeasure ||
                primitive.PartNumber is null ||
                primitive.MeasureNumber is null)
                continue;

            var partNumber = primitive.PartNumber.Value;
            var measureNumber = primitive.MeasureNumber.Value;
            if (!grid.TryGetBlock(partNumber, measureNumber, out var block))
                continue;

            var oval = TryBuildOval(primitive, block);
            if (oval is not null)
                ovalCandidates.Add(oval);
        }

        var diagnostics = new List<NoteHeadDiagnosticEntry>();
        var result = new List<NoteHeadResolution>();

        var orderedCandidates = ovalCandidates
            .OrderBy(x => x.PartNumber)
            .ThenBy(x => x.MeasureNumber)
            .ThenBy(x => LogicalCenterX(x.LogicalBounds) ?? double.MinValue)
            .ThenBy(x => x.LogicalBounds.Top)
            .ToArray();

        foreach (var outer in orderedCandidates)
        {
            var hollowContourDetected = HasHollowSourceContour(outer) ||
                                        HasNestedSameSourceOval(outer, ovalCandidates);

            var legalStaffPosition = IsOnLegalStaffPosition(outer, ledgerLines);
            if (!legalStaffPosition)
            {
                diagnostics.Add(Diagnostic(
                    outer,
                    accepted: false,
                    isFilled: null,
                    pitch: null,
                    verdict: "rejected: not on staff/ledger position",
                    hollowContourDetected));
                continue;
            }

            var largerCandidates = ovalCandidates
                .Where(x => x.Id != outer.Id)
                .Where(x => x.PartNumber == outer.PartNumber && x.MeasureNumber == outer.MeasureNumber)
                .Where(x => Area(x.PhysicalBounds) > Area(outer.PhysicalBounds))
                .ToArray();

            var containingOuter = largerCandidates
                .Any(x => Contains(x.PhysicalBounds, outer.PhysicalBounds));
            if (containingOuter)
            {
                diagnostics.Add(Diagnostic(
                    outer,
                    accepted: false,
                    isFilled: null,
                    pitch: null,
                    verdict: "rejected: contained by larger oval candidate",
                    hollowContourDetected));
                continue;
            }

            var clef = FindNearestClefToLeft(outer, clefs);
            if (clef is null)
            {
                diagnostics.Add(Diagnostic(
                    outer,
                    accepted: false,
                    isFilled: null,
                    pitch: null,
                    verdict: "rejected: no clef to the left",
                    hollowContourDetected));
                continue;
            }

            if (clef.Kind == ClefKind.C)
            {
                diagnostics.Add(Diagnostic(
                    outer,
                    accepted: false,
                    isFilled: null,
                    pitch: null,
                    verdict: "rejected: C clef pitch mapping is not implemented",
                    hollowContourDetected));
                continue;
            }

            var logicalCenterY = (outer.LogicalBounds.Top + outer.LogicalBounds.Bottom) / 2.0;
            var staffPosition = (int)Math.Round(logicalCenterY);
            var pitch = PitchFor(clef.Kind, staffPosition);
            var isFilled = !hollowContourDetected;

            result.Add(new NoteHeadResolution(
                outer.PartNumber,
                outer.MeasureNumber,
                outer.LogicalBounds,
                outer.PhysicalBounds,
                isFilled,
                pitch));

            diagnostics.Add(Diagnostic(
                outer,
                accepted: true,
                isFilled,
                pitch,
                verdict: isFilled ? $"accepted: {pitch} filled" : $"accepted: {pitch} hollow",
                hollowContourDetected));
        }

        LastDiagnostics = diagnostics;

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.LogicalBounds.Left ?? double.MinValue)
            .ThenBy(x => x.LogicalBounds.Top)
            .ToArray();
    }

    private static NoteHeadDiagnosticEntry Diagnostic(
        OvalCandidate candidate,
        bool accepted,
        bool? isFilled,
        string? pitch,
        string verdict,
        bool hollowContourDetected) =>
        new(
            candidate.Id,
            candidate.PartNumber,
            candidate.MeasureNumber,
            candidate.PhysicalBounds,
            candidate.LogicalBounds,
            candidate.Contour,
            candidate.SourceGroupContours,
            accepted,
            isFilled,
            pitch,
            verdict,
            hollowContourDetected);

    private OvalCandidate? TryBuildOval(ResolvedPrimitive primitive, LogicalGridBlock block)
    {
        var bounds = primitive.PhysicalBounds;
        var staffSpace = block.PhysicalBounds.Height / 4.0;
        if (staffSpace <= 1e-9 || bounds.Height <= 1e-9)
            return null;

        var heightInStaffSpaces = bounds.Height / staffSpace;
        if (heightInStaffSpaces < MinHeightInStaffSpaces || heightInStaffSpaces > MaxHeightInStaffSpaces)
            return null;

        var aspectRatio = bounds.Width / bounds.Height;
        if (aspectRatio < MinAspectRatio || aspectRatio > MaxAspectRatio)
            return null;

        var points = primitive.Contour.Points;
        if (points.Count < 12)
            return null;

        var radialVariation = RadialVariation(points, bounds);
        if (radialVariation > MaxRadialVariation)
            return null;

        var logical = block.ToLogical(bounds);
        var centerY = (logical.Top + logical.Bottom) / 2.0;
        var nearestGridPosition = Math.Round(centerY);
        if (Math.Abs(centerY - nearestGridPosition) > MaxLogicalYCenterOffset)
            return null;

        return new OvalCandidate(
            primitive.Id,
            primitive.PartNumber!.Value,
            primitive.MeasureNumber!.Value,
            bounds,
            logical,
            primitive.Contour,
            primitive.Source,
            primitive.SourceGroupContours);
    }

    /// <summary>
    /// Some SVG glyphs expose the white hole as another contour inside the source contour group.
    /// Keep this as a cheap direct signal when it is available.
    /// </summary>
    private bool HasHollowSourceContour(OvalCandidate outer)
    {
        if (outer.SourceGroupContours is null || outer.SourceGroupContours.Count < 2)
            return false;

        var contourBounds = outer.SourceGroupContours
            .Select(TryGetBounds)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .ToArray();

        var smallerContours = contourBounds
            .Where(x => Area(x) < Area(outer.PhysicalBounds) * 0.95)
            .ToArray();

        var containedContours = smallerContours
            .Where(x => Contains(outer.PhysicalBounds, x))
            .ToArray();

        var centeredContours = containedContours
            .Where(x => CenterDistance(outer.PhysicalBounds, x) <= outer.PhysicalBounds.Height * 0.35)
            .ToArray();

        var correctlySizedContours = centeredContours
            .Where(x => WidthRatio(x, outer.PhysicalBounds) >= HollowInnerMinWidthRatio)
            .Where(x => WidthRatio(x, outer.PhysicalBounds) <= HollowInnerMaxWidthRatio)
            .ToArray();

        return correctlySizedContours.Length > 0;
    }

    /// <summary>
    /// In real score SVGs a hollow head is often split into two PrimitiveResolver artifacts:
    /// a larger outer oval and a smaller inner oval. The inner oval is then rejected later as
    /// "contained by larger oval candidate". Treat that nested oval as the white hole only when
    /// both artifacts have the same source provenance; this prevents a nearby note in a chord from
    /// accidentally making another head hollow.
    /// </summary>
    private bool HasNestedSameSourceOval(
        OvalCandidate outer,
        IReadOnlyList<OvalCandidate> allCandidates)
    {
        var nestedCandidates = allCandidates
            .Where(x => x.Id != outer.Id)
            .Where(x => x.PartNumber == outer.PartNumber && x.MeasureNumber == outer.MeasureNumber)
            .Where(x => SameSourceProvenance(outer.Source, x.Source))
            .Where(x => Area(x.PhysicalBounds) < Area(outer.PhysicalBounds) * 0.95)
            .Where(x => Contains(outer.PhysicalBounds, x.PhysicalBounds))
            .Where(x => CenterDistance(outer.PhysicalBounds, x.PhysicalBounds) <= outer.PhysicalBounds.Height * 0.35)
            .Where(x => WidthRatio(x.PhysicalBounds, outer.PhysicalBounds) >= HollowInnerMinWidthRatio)
            .Where(x => WidthRatio(x.PhysicalBounds, outer.PhysicalBounds) <= HollowInnerMaxWidthRatio)
            .ToArray();

        return nestedCandidates.Length > 0;
    }

    private static bool SameSourceProvenance(PrimitiveSourceRef a, PrimitiveSourceRef b)
    {
        if (!string.IsNullOrWhiteSpace(a.GroupAnchor) &&
            !string.IsNullOrWhiteSpace(b.GroupAnchor))
            return string.Equals(a.GroupAnchor, b.GroupAnchor, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(a.Anchor) &&
            !string.IsNullOrWhiteSpace(b.Anchor))
            return string.Equals(a.Anchor, b.Anchor, StringComparison.Ordinal);

        return false;
    }

    private static RectD? TryGetBounds(PrimitiveContour contour)
    {
        if (contour.Points.Count == 0)
            return null;

        var left = contour.Points.Min(x => (double)x.X);
        var top = contour.Points.Min(x => (double)x.Y);
        var right = contour.Points.Max(x => (double)x.X);
        var bottom = contour.Points.Max(x => (double)x.Y);
        return new RectD(left, top, right, bottom);
    }

    private static bool IsOnLegalStaffPosition(
        OvalCandidate candidate,
        IReadOnlyList<LedgerLineResolution> ledgerLines)
    {
        var centerY = (candidate.LogicalBounds.Top + candidate.LogicalBounds.Bottom) / 2.0;
        var position = (int)Math.Round(centerY);

        if (position >= 0 && position <= 8)
            return true;

        if (position is -1 or 9)
            return true;

        var requiredDepth = position < -1
            ? (int)Math.Ceiling((Math.Abs(position) - 1) / 2.0)
            : (int)Math.Ceiling((position - 9) / 2.0);

        var centerX = LogicalCenterX(candidate.LogicalBounds);
        if (centerX is null)
            return false;

        var matchingLadders = ledgerLines
            .Where(x => x.PartNumber == candidate.PartNumber && x.MeasureNumber == candidate.MeasureNumber)
            .Where(x => Math.Sign(x.Depth) == (position < 0 ? -1 : 1))
            .Where(x => Math.Abs(x.Depth) >= requiredDepth)
            .ToArray();

        return matchingLadders
            .Any(x => ContainsLogicalX(x.LogicalBounds, centerX.Value));
    }

    private static ClefResolution? FindNearestClefToLeft(
        OvalCandidate note,
        IReadOnlyList<ClefResolution> clefs)
    {
        var noteX = LogicalCenterX(note.LogicalBounds) ?? double.MaxValue;

        var eligible = clefs
            .Where(x => x.PartNumber == note.PartNumber)
            .Where(x => x.MeasureNumber < note.MeasureNumber ||
                        (x.MeasureNumber == note.MeasureNumber &&
                         (LogicalCenterX(x.LogicalBounds) ?? double.MinValue) <= noteX))
            .ToArray();

        return eligible
            .OrderByDescending(x => x.MeasureNumber)
            .ThenByDescending(x => LogicalCenterX(x.LogicalBounds) ?? double.MinValue)
            .FirstOrDefault();
    }

    private static string PitchFor(ClefKind clef, int staffPosition)
    {
        var reference = clef switch
        {
            ClefKind.G => DiatonicIndex('F', 5),
            ClefKind.F => DiatonicIndex('A', 3),
            _ => throw new InvalidOperationException("C clef pitch mapping is not implemented yet.")
        };

        var index = reference - staffPosition;
        return PitchName(index);
    }

    private static int DiatonicIndex(char note, int octave)
    {
        var letterIndex = note switch
        {
            'C' => 0, 'D' => 1, 'E' => 2, 'F' => 3,
            'G' => 4, 'A' => 5, 'B' => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(note))
        };
        return octave * 7 + letterIndex;
    }

    private static string PitchName(int index)
    {
        var octave = (int)Math.Floor(index / 7.0);
        var letterIndex = index - octave * 7;
        var letter = "CDEFGAB"[letterIndex];
        return $"{letter}{octave}";
    }

    private static double RadialVariation(IReadOnlyList<System.Numerics.Vector2> points, RectD bounds)
    {
        var rx = Math.Max(1e-9, bounds.Width / 2.0);
        var ry = Math.Max(1e-9, bounds.Height / 2.0);
        var cx = bounds.CenterX;
        var cy = bounds.CenterY;

        var radii = points
            .Select(p => Math.Sqrt(
                Math.Pow((p.X - cx) / rx, 2) +
                Math.Pow((p.Y - cy) / ry, 2)))
            .Where(x => double.IsFinite(x))
            .ToArray();

        if (radii.Length == 0)
            return double.MaxValue;

        var mean = radii.Average();
        if (mean <= 1e-9)
            return double.MaxValue;

        var variance = radii
            .Select(x => (x - mean) * (x - mean))
            .Average();

        return Math.Sqrt(variance) / mean;
    }

    private static bool Contains(RectD outer, RectD inner) =>
        inner.Left >= outer.Left && inner.Right <= outer.Right &&
        inner.Top >= outer.Top && inner.Bottom <= outer.Bottom;

    private static bool ContainsLogicalX(LogicalRectD bounds, double x) =>
        bounds.Left is { } left && bounds.Right is { } right && x >= left && x <= right;

    private static double? LogicalCenterX(LogicalRectD bounds) =>
        bounds.Left is { } left && bounds.Right is { } right ? (left + right) / 2.0 : null;

    private static double WidthRatio(RectD inner, RectD outer) =>
        inner.Width / Math.Max(1e-9, outer.Width);

    private static double CenterDistance(RectD a, RectD b)
    {
        var dx = a.CenterX - b.CenterX;
        var dy = a.CenterY - b.CenterY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Area(RectD bounds) => bounds.Width * bounds.Height;

    private sealed record OvalCandidate(
        int Id,
        int PartNumber,
        int MeasureNumber,
        RectD PhysicalBounds,
        LogicalRectD LogicalBounds,
        PrimitiveContour Contour,
        PrimitiveSourceRef Source,
        IReadOnlyList<PrimitiveContour>? SourceGroupContours);
}
