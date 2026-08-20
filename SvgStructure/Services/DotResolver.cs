using System.Numerics;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds small round augmentation dots to the right of already recognized notes or rests.
/// Matching is one-to-one: once a note/rest has received a dot it cannot receive another dot.
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

    public IReadOnlyList<DotResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<RestResolution> rests)
    {
        var dotCandidates = BuildDotCandidates(primitives, grid);
        var targets = BuildTargets(noteHeads, rests);
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

        // Global greedy matching is preferable to resolving dots one by one: the best geometric
        // pair wins first, then both the dot and its target are removed from further consideration.
        // In particular, vertically stacked dots can never attach to the same note/rest.
        var usedDots = new HashSet<int>();
        var usedTargets = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DotResolution>();

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
                (note.LogicalBounds.Top + note.LogicalBounds.Bottom) / 2.0,
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
}
