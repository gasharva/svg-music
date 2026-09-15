using System.Numerics;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds small round augmentation dots to the right of already recognized notes or rests.
/// Vertically stacked note heads and dots are matched as groups before the ordinary one-to-one
/// fallback, so engraving offsets cannot make neighbouring notes steal each other's dots.
/// This pass is deliberately geometric: no PCA/model recognition is used.
/// </summary>
public sealed class DotResolver
{
    public double MaxWidthInStaffSpaces { get; init; } = 0.55;
    public double MaxHeightInStaffSpaces { get; init; } = 0.55;
    public double MinSizeInStaffSpaces { get; init; } = 0.06;
    public double MinAspectRatio { get; init; } = 0.55;
    public double MaxAspectRatio { get; init; } = 1.80;
    public double MinCircularity { get; init; } = 0.64;
    public double MaxLogicalDistanceToTarget { get; init; } = 4.0;
    public double MaxVerticalErrorLogical { get; init; } = 0.70;
    public double MaxAreaFractionOfNoteHead { get; init; } = 0.35;
    public double MaxDimensionFractionOfNoteHead { get; init; } = 0.72;

    /// <summary>
    /// Horizontal tolerance used when note-head/dot bounding boxes almost touch.
    /// Expressed in physical staff spaces and converted separately for every P+M block.
    /// </summary>
    public double ColumnXJitterInStaffSpaces { get; init; } = 0.20;

    /// <summary>
    /// Maximum physical gap between a note-head snowman and its dot column.
    /// Pair-level logical X limits still apply as a second guard.
    /// </summary>
    public double MaxColumnHorizontalGapInStaffSpaces { get; init; } = 2.5;

    public IReadOnlyList<DotResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<RestResolution> rests)
    {
        var dotCandidates = BuildDotCandidates(primitives, grid);
        var targets = BuildTargets(noteHeads, rests);

        var usedDots = new HashSet<int>();
        var usedTargets = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DotResolution>();

        // First solve the ambiguous case that motivated this pass: a vertical column of dots beside
        // a vertical column of same-kind note heads. The assignment is global and monotonic in Y,
        // so an upper dot cannot greedily consume the note that a lower dot needs.
        var noteKeys = new Dictionary<NoteHeadResolution, string>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < noteHeads.Count; i++)
            noteKeys[noteHeads[i]] = $"n:{i}";

        foreach (var columnMatch in BuildColumnMatches(dotCandidates, noteHeads, grid)
                     .OrderByDescending(x => x.Pairs.Count)
                     .ThenBy(x => x.Score))
        {
            if (columnMatch.Pairs.Any(x => usedDots.Contains(x.Dot.PrimitiveId)))
                continue;
            if (columnMatch.Pairs.Any(x => !noteKeys.TryGetValue(x.Note, out var key) || usedTargets.Contains(key)))
                continue;

            foreach (var pair in columnMatch.Pairs)
            {
                var key = noteKeys[pair.Note];
                usedDots.Add(pair.Dot.PrimitiveId);
                usedTargets.Add(key);

                result.Add(new DotResolution(
                    pair.Dot.PrimitiveId,
                    pair.Dot.PartNumber,
                    pair.Dot.MeasureNumber,
                    pair.Dot.LogicalBounds,
                    pair.Dot.PhysicalBounds,
                    pair.Note,
                    null));
            }
        }

        // Preserve the previous behaviour for everything that is not a real stacked-column case:
        // single dots, rests, and any column candidate that could not be matched consistently.
        var pairings = new List<Pairing>();

        foreach (var dot in dotCandidates)
        {
            foreach (var target in targets)
            {
                if (dot.PartNumber != target.PartNumber || dot.MeasureNumber != target.MeasureNumber)
                    continue;

                var dx = dot.LogicalX - target.LogicalX;
                if (dx <= 0 || dx >= MaxLogicalDistanceToTarget)
                    continue;

                if (target.Note is not null && !IsMuchSmallerThanNote(dot.PhysicalBounds, target.PhysicalBounds))
                    continue;

                // A dot may sit beside the target or half a staff-space above/below it,
                // regardless of whether the note itself is on a line or in a space.
                var dy = VerticalError(dot.LogicalY, target.LogicalY);
                if (dy > MaxVerticalErrorLogical)
                    continue;

                pairings.Add(new Pairing(dot, target, dx + dy * 1.75));
            }
        }

        // Global greedy matching remains the fallback. Group assignments above have already reserved
        // their dots and note targets, so this can only fill still-free notes/rests.
        foreach (var pairing in pairings.OrderBy(x => x.Score).ThenBy(x => x.Dot.PhysicalBounds.Left))
        {
            if (!usedDots.Add(pairing.Dot.PrimitiveId))
                continue;
            if (!usedTargets.Add(pairing.Target.Key))
            {
                usedDots.Remove(pairing.Dot.PrimitiveId);
                continue;
            }

            result.Add(new DotResolution(
                pairing.Dot.PrimitiveId,
                pairing.Dot.PartNumber,
                pairing.Dot.MeasureNumber,
                pairing.Dot.LogicalBounds,
                pairing.Dot.PhysicalBounds,
                pairing.Target.Note,
                pairing.Target.Rest));
        }

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ThenBy(x => x.PhysicalBounds.Top)
            .ToArray();
    }

    private IReadOnlyList<ColumnMatch> BuildColumnMatches(
        IReadOnlyList<DotCandidate> dots,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        LogicalGridResolution grid)
    {
        var result = new List<ColumnMatch>();

        foreach (var scope in dots.GroupBy(x => (x.PartNumber, x.MeasureNumber)))
        {
            if (!grid.TryGetBlock(scope.Key.PartNumber, scope.Key.MeasureNumber, out var block))
                continue;

            var staffSpace = block.PhysicalBounds.Height / 4.0;
            if (staffSpace <= 1e-9)
                continue;

            var xJitter = staffSpace * ColumnXJitterInStaffSpaces;
            var dotColumns = XColumnHelper.GroupByX(
                scope,
                x => x.PhysicalBounds.Left,
                x => x.PhysicalBounds.Right,
                x => x.LogicalY,
                xJitter);

            var scopedNotes = noteHeads
                .Where(x => x.PartNumber == scope.Key.PartNumber &&
                            x.MeasureNumber == scope.Key.MeasureNumber)
                .ToArray();

            var noteColumns = NoteHeadColumnHelper.GroupByX(scopedNotes, xJitter);

            foreach (var dotColumn in dotColumns.Where(x => x.Items.Count >= 2))
            {
                foreach (var noteColumn in noteColumns.Where(x => x.NoteHeads.Count >= 2))
                {
                    var match = TryMatchColumns(dotColumn, noteColumn, staffSpace);
                    if (match is not null)
                        result.Add(match);
                }
            }
        }

        return result;
    }

    private ColumnMatch? TryMatchColumns(
        XColumn<DotCandidate> dots,
        NoteHeadColumn notes,
        double staffSpace)
    {
        if (dots.Items.Count > notes.NoteHeads.Count)
            return null;

        // The whole dot column must be on the right-hand side of the note-head snowman.
        if (dots.CenterX <= notes.CenterX)
            return null;

        var columnGap = Math.Max(0, dots.Left - notes.Right) / staffSpace;
        if (columnGap > MaxColumnHorizontalGapInStaffSpaces)
            return null;

        var orderedDots = dots.Items
            .OrderBy(x => x.LogicalY)
            .ToArray();
        var orderedNotes = notes.NoteHeads
            .OrderBy(CenterY)
            .ToArray();

        var dotCount = orderedDots.Length;
        var noteCount = orderedNotes.Length;
        var dp = new double[dotCount + 1, noteCount + 1];
        var action = new MatchAction[dotCount + 1, noteCount + 1];

        for (var d = 0; d <= dotCount; d++)
        {
            for (var n = 0; n <= noteCount; n++)
                dp[d, n] = double.PositiveInfinity;
        }

        // With no dots matched yet, any number of leading notes may remain undotted for free.
        for (var n = 0; n <= noteCount; n++)
            dp[0, n] = 0;

        for (var d = 1; d <= dotCount; d++)
        {
            for (var n = 1; n <= noteCount; n++)
            {
                // Leave this note without a visual dot.
                if (dp[d, n - 1] < dp[d, n])
                {
                    dp[d, n] = dp[d, n - 1];
                    action[d, n] = MatchAction.SkipNote;
                }

                // Or match the current dot to the current note. Because both sequences are ordered
                // by Y, this transition can never produce crossing assignments.
                var previous = dp[d - 1, n - 1];
                if (double.IsPositiveInfinity(previous))
                    continue;

                var pairScore = ColumnPairScore(orderedDots[d - 1], orderedNotes[n - 1], staffSpace);
                if (double.IsPositiveInfinity(pairScore))
                    continue;

                var matched = previous + pairScore;
                if (matched <= dp[d, n])
                {
                    dp[d, n] = matched;
                    action[d, n] = MatchAction.Match;
                }
            }
        }

        if (double.IsPositiveInfinity(dp[dotCount, noteCount]))
            return null;

        var pairs = new List<ColumnPair>();
        var dotIndex = dotCount;
        var noteIndex = noteCount;

        while (dotIndex > 0)
        {
            if (noteIndex <= 0)
                return null;

            switch (action[dotIndex, noteIndex])
            {
                case MatchAction.Match:
                {
                    var dot = orderedDots[dotIndex - 1];
                    var note = orderedNotes[noteIndex - 1];
                    pairs.Add(new ColumnPair(dot, note));
                    dotIndex--;
                    noteIndex--;
                    break;
                }
                case MatchAction.SkipNote:
                    noteIndex--;
                    break;
                default:
                    return null;
            }
        }

        pairs.Reverse();

        // All dots in this column must participate. The mean score lets columns with different
        // cardinalities compete fairly; the caller separately prefers larger valid snowmen.
        return new ColumnMatch(
            pairs,
            dp[dotCount, noteCount] / Math.Max(1, dotCount));
    }

    private double ColumnPairScore(
        DotCandidate dot,
        NoteHeadResolution note,
        double staffSpace)
    {
        var noteX = Center(note.LogicalBounds.Left, note.LogicalBounds.Right);
        if (noteX is null)
            return double.PositiveInfinity;

        var dx = dot.LogicalX - noteX.Value;
        if (dx <= 0 || dx >= MaxLogicalDistanceToTarget)
            return double.PositiveInfinity;

        if (!IsMuchSmallerThanNote(dot.PhysicalBounds, note.PhysicalBounds))
            return double.PositiveInfinity;

        var noteY = CenterY(note);
        var dy = VerticalError(dot.LogicalY, noteY);
        if (dy > MaxVerticalErrorLogical)
            return double.PositiveInfinity;

        // Y carries the musical row information; X is deliberately only a tie-breaker inside an
        // already-established pair of columns. A small physical-gap term stabilizes staggered heads.
        var physicalGap = Math.Max(0, dot.PhysicalBounds.Left - note.PhysicalBounds.Right) /
                          Math.Max(staffSpace, 1e-9);

        return dy * 3.0 + dx * 0.20 + physicalGap * 0.10;
    }

    private IReadOnlyList<DotCandidate> BuildDotCandidates(
        PrimitiveResolution primitives,
        LogicalGridResolution grid)
    {
        var result = new List<DotCandidate>();

        foreach (var primitive in primitives.Primitives)
        {
            if (primitive.PartNumber is not { } partNumber ||
                primitive.MeasureNumber is not { } measureNumber)
                continue;

            if (!grid.TryGetBlock(partNumber, measureNumber, out var block))
                continue;

            var staffSpace = block.PhysicalBounds.Height / 4.0;
            if (!LooksLikeDot(primitive, staffSpace))
                continue;

            var logical = block.ToLogical(primitive.PhysicalBounds);
            var logicalX = Center(logical.Left, logical.Right);
            if (logicalX is null)
                continue;

            result.Add(new DotCandidate(
                primitive.Id,
                partNumber,
                measureNumber,
                logical,
                primitive.PhysicalBounds,
                logicalX.Value,
                (logical.Top + logical.Bottom) / 2.0));
        }

        return result;
    }

    private static IReadOnlyList<TargetCandidate> BuildTargets(
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<RestResolution> rests)
    {
        var result = new List<TargetCandidate>();

        for (var i = 0; i < noteHeads.Count; i++)
        {
            var note = noteHeads[i];
            var x = Center(note.LogicalBounds.Left, note.LogicalBounds.Right);
            if (x is null)
                continue;

            result.Add(new TargetCandidate(
                $"n:{i}",
                note.PartNumber,
                note.MeasureNumber,
                x.Value,
                CenterY(note),
                note.PhysicalBounds,
                note,
                null));
        }

        for (var i = 0; i < rests.Count; i++)
        {
            var rest = rests[i];
            var x = Center(rest.LogicalBounds.Left, rest.LogicalBounds.Right);
            if (x is null)
                continue;

            result.Add(new TargetCandidate(
                $"r:{i}",
                rest.PartNumber,
                rest.MeasureNumber,
                x.Value,
                (rest.LogicalBounds.Top + rest.LogicalBounds.Bottom) / 2.0,
                rest.PhysicalBounds,
                null,
                rest));
        }

        return result;
    }

    private static double CenterY(NoteHeadResolution note) =>
        (note.LogicalBounds.Top + note.LogicalBounds.Bottom) / 2.0;

    private static double VerticalError(double dotY, double targetY) =>
        new[]
        {
            Math.Abs(dotY - targetY),
            Math.Abs(dotY - (targetY - 1.0)),
            Math.Abs(dotY - (targetY + 1.0))
        }.Min();

    private bool LooksLikeDot(ResolvedPrimitive primitive, double staffSpace)
    {
        var b = primitive.PhysicalBounds;
        if (staffSpace <= 1e-9 || b.Width <= 1e-9 || b.Height <= 1e-9)
            return false;

        var width = b.Width / staffSpace;
        var height = b.Height / staffSpace;
        if (width < MinSizeInStaffSpaces || height < MinSizeInStaffSpaces)
            return false;
        if (width > MaxWidthInStaffSpaces || height > MaxHeightInStaffSpaces)
            return false;

        var aspect = b.Width / b.Height;
        if (aspect < MinAspectRatio || aspect > MaxAspectRatio)
            return false;

        return Circularity(primitive.Contour.Points) >= MinCircularity;
    }

    private bool IsMuchSmallerThanNote(RectD dot, RectD note)
    {
        if (note.Width <= 1e-9 || note.Height <= 1e-9)
            return false;

        var dotArea = dot.Width * dot.Height;
        var noteArea = note.Width * note.Height;
        if (dotArea > noteArea * MaxAreaFractionOfNoteHead)
            return false;

        return dot.Width <= note.Width * MaxDimensionFractionOfNoteHead &&
               dot.Height <= note.Height * MaxDimensionFractionOfNoteHead;
    }

    private static double Circularity(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 4)
            return 0;

        double area2 = 0;
        double perimeter = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area2 += (double)a.X * b.Y - (double)b.X * a.Y;
            var dx = (double)b.X - a.X;
            var dy = (double)b.Y - a.Y;
            perimeter += Math.Sqrt(dx * dx + dy * dy);
        }

        var area = Math.Abs(area2) / 2.0;
        if (area <= 1e-9 || perimeter <= 1e-9)
            return 0;

        return 4.0 * Math.PI * area / (perimeter * perimeter);
    }

    private static double? Center(double? left, double? right) =>
        left is { } l && right is { } r ? (l + r) / 2.0 : null;

    private sealed record DotCandidate(
        int PrimitiveId,
        int PartNumber,
        int MeasureNumber,
        LogicalRectD LogicalBounds,
        RectD PhysicalBounds,
        double LogicalX,
        double LogicalY);

    private sealed record TargetCandidate(
        string Key,
        int PartNumber,
        int MeasureNumber,
        double LogicalX,
        double LogicalY,
        RectD PhysicalBounds,
        NoteHeadResolution? Note,
        RestResolution? Rest);

    private sealed record Pairing(DotCandidate Dot, TargetCandidate Target, double Score);
    private sealed record ColumnPair(DotCandidate Dot, NoteHeadResolution Note);
    private sealed record ColumnMatch(IReadOnlyList<ColumnPair> Pairs, double Score);

    private enum MatchAction
    {
        None,
        SkipNote,
        Match
    }
}
