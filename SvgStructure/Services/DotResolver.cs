using System.Numerics;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds small round augmentation dots to the right of already recognized note heads.
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
    public double MaxLogicalDistanceToNote { get; init; } = 4.0;
    public double MaxVerticalErrorLogical { get; init; } = 0.70;
    public double LineSnapToleranceLogical { get; init; } = 0.40;
    public double MaxAreaFractionOfNoteHead { get; init; } = 0.35;
    public double MaxDimensionFractionOfNoteHead { get; init; } = 0.72;

    public IReadOnlyList<DotResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads)
    {
        var result = new List<DotResolution>();

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

            var dotLogical = block.ToLogical(primitive.PhysicalBounds);
            var dotCenterX = Center(dotLogical.Left, dotLogical.Right);
            var dotCenterY = (dotLogical.Top + dotLogical.Bottom) / 2.0;
            if (dotCenterX is null)
                continue;

            var note = FindAttachedNote(
                primitive.PhysicalBounds,
                dotCenterX.Value,
                dotCenterY,
                partNumber,
                measureNumber,
                noteHeads);
            if (note is null)
                continue;

            result.Add(new DotResolution(
                primitive.Id,
                partNumber,
                measureNumber,
                dotLogical,
                primitive.PhysicalBounds,
                note));
        }

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ThenBy(x => x.PhysicalBounds.Top)
            .ToArray();
    }

    private NoteHeadResolution? FindAttachedNote(
        RectD dotBounds,
        double dotLogicalX,
        double dotLogicalY,
        int partNumber,
        int measureNumber,
        IReadOnlyList<NoteHeadResolution> noteHeads)
    {
        return noteHeads
            .Where(x => x.PartNumber == partNumber && x.MeasureNumber == measureNumber)
            .Select(note => new
            {
                Note = note,
                NoteX = Center(note.LogicalBounds.Left, note.LogicalBounds.Right),
                NoteY = (note.LogicalBounds.Top + note.LogicalBounds.Bottom) / 2.0
            })
            .Where(x => x.NoteX is not null)
            .Select(x => new
            {
                x.Note,
                Dx = dotLogicalX - x.NoteX!.Value,
                Dy = VerticalError(dotLogicalY, x.NoteY),
                SizeOk = IsMuchSmallerThanNote(dotBounds, x.Note.PhysicalBounds)
            })
            .Where(x => x.SizeOk)
            .Where(x => x.Dx > 0 && x.Dx < MaxLogicalDistanceToNote)
            .Where(x => x.Dy <= MaxVerticalErrorLogical)
            .OrderBy(x => x.Dx + x.Dy * 1.75)
            .Select(x => x.Note)
            .FirstOrDefault();
    }

    private double VerticalError(double dotY, double noteY)
    {
        var nearestLine = Math.Round(noteY / 2.0) * 2.0;
        var noteIsOnLine = Math.Abs(noteY - nearestLine) <= LineSnapToleranceLogical;

        if (!noteIsOnLine)
            return Math.Abs(dotY - noteY);

        // Staff and ledger lines are all even logical Y levels. A dot belonging to a
        // line note is conventionally moved into either adjacent space: +/- 1 logical Y.
        return Math.Min(
            Math.Abs(dotY - (nearestLine - 1.0)),
            Math.Abs(dotY - (nearestLine + 1.0)));
    }

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
}
