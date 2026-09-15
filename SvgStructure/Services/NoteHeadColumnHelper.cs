using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// A transient vertical stack of note heads whose physical X ranges overlap, touch,
/// or are connected transitively through a small horizontal jitter.
/// Columns are intentionally not persisted in the recognition model: callers can
/// recompute them cheaply whenever chord-like vertical geometry is needed.
/// </summary>
public sealed record NoteHeadColumn(
    int PartNumber,
    int MeasureNumber,
    bool IsFilled,
    IReadOnlyList<NoteHeadResolution> NoteHeads,
    double Left,
    double Right)
{
    public double CenterX => (Left + Right) / 2.0;
}

/// <summary>
/// Finds vertical "snowmen" of note heads by X geometry.
/// Filled and hollow heads are always kept in separate columns.
/// </summary>
public static class NoteHeadColumnHelper
{
    public static IReadOnlyList<NoteHeadColumn> GroupByX(
        IEnumerable<NoteHeadResolution> noteHeads,
        double xJitter)
    {
        if (xJitter < 0)
            throw new ArgumentOutOfRangeException(nameof(xJitter));

        var result = new List<NoteHeadColumn>();

        foreach (var scope in noteHeads.GroupBy(x => (x.PartNumber, x.MeasureNumber, x.IsFilled)))
        {
            foreach (var column in XColumnHelper.GroupByX(
                         scope,
                         x => x.PhysicalBounds.Left,
                         x => x.PhysicalBounds.Right,
                         x => x.PhysicalBounds.CenterY,
                         xJitter))
            {
                result.Add(new NoteHeadColumn(
                    scope.Key.PartNumber,
                    scope.Key.MeasureNumber,
                    scope.Key.IsFilled,
                    column.Items,
                    column.Left,
                    column.Right));
            }
        }

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PartNumber)
            .ThenBy(x => x.Left)
            .ThenBy(x => x.NoteHeads[0].PhysicalBounds.CenterY)
            .ToArray();
    }
}

/// <summary>
/// Cheap X-only connected-component grouping shared by note-head and dot columns.
/// If A touches B and B touches C (within jitter), all three belong to one column
/// even when A and C do not overlap directly.
/// </summary>
internal static class XColumnHelper
{
    public static IReadOnlyList<XColumn<T>> GroupByX<T>(
        IEnumerable<T> items,
        Func<T, double> getLeft,
        Func<T, double> getRight,
        Func<T, double> getCenterY,
        double xJitter)
    {
        if (xJitter < 0)
            throw new ArgumentOutOfRangeException(nameof(xJitter));

        var ordered = items
            .OrderBy(getLeft)
            .ThenBy(getCenterY)
            .ToArray();

        if (ordered.Length == 0)
            return Array.Empty<XColumn<T>>();

        var result = new List<XColumn<T>>();
        var current = new List<T> { ordered[0] };
        var currentLeft = getLeft(ordered[0]);
        var currentRight = getRight(ordered[0]);

        for (var i = 1; i < ordered.Length; i++)
        {
            var item = ordered[i];
            var left = getLeft(item);
            var right = getRight(item);

            if (left <= currentRight + xJitter)
            {
                current.Add(item);
                currentLeft = Math.Min(currentLeft, left);
                currentRight = Math.Max(currentRight, right);
                continue;
            }

            AddCurrent();
            current = new List<T> { item };
            currentLeft = left;
            currentRight = right;
        }

        AddCurrent();
        return result;

        void AddCurrent()
        {
            result.Add(new XColumn<T>(
                current.OrderBy(getCenterY).ToArray(),
                currentLeft,
                currentRight));
        }
    }
}

internal sealed record XColumn<T>(
    IReadOnlyList<T> Items,
    double Left,
    double Right)
{
    public double CenterX => (Left + Right) / 2.0;
}
