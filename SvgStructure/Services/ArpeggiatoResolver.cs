using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Finds vertical arpeggiato marks geometrically, without PCA.
/// A candidate is a narrow, sufficiently tall primitive with repeated horizontal oscillation.
/// Several vertically adjacent candidates on the same X may form one logical arpeggiato.
/// A chord of at least two note heads must sit immediately to the right on one logical X.
/// </summary>
public sealed class ArpeggiatoResolver
{
    public double MinTotalHeightInStaffSpaces { get; init; } = 2.0; // 50% of a 4-space staff.
    public double MinFragmentHeightInStaffSpaces { get; init; } = 0.45;
    public double MaxWidthInStaffSpaces { get; init; } = 2.0;
    public double MaxAlignedXDistanceInStaffSpaces { get; init; } = 0.55;
    public double MaxFragmentGapInStaffSpaces { get; init; } = 1.0;
    public double MaxNoteDistanceLogicalUnits { get; init; } = 3.0;
    public double NoteColumnToleranceLogicalUnits { get; init; } = 0.45;
    public int MinWaveDirectionChanges { get; init; } = 2;
    public double MinWaveAmplitudeInStaffSpaces { get; init; } = 0.08;
    public int SliceCount { get; init; } = 24;

    public IReadOnlyList<ArpeggiatoResolution> Resolve(
        PrimitiveResolution primitives,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads)
    {
        var fragments = BuildFragments(primitives, grid);
        var groups = GroupAlignedFragments(fragments);
        var result = new List<ArpeggiatoResolution>();

        foreach (var group in groups)
        {
            var bounds = Union(group.Select(x => x.Bounds));
            var staffSpace = group.Average(x => x.StaffSpace);
            if (staffSpace <= 1e-9 || bounds.Height / staffSpace < MinTotalHeightInStaffSpaces)
                continue;

            var measureNumber = group[0].MeasureNumber;
            if (group.Any(x => x.MeasureNumber != measureNumber))
                continue;

            var chord = FindChordToRight(bounds, measureNumber, grid, noteHeads);
            if (chord is null)
                continue;

            result.Add(new ArpeggiatoResolution(
                measureNumber,
                bounds,
                group.Select(x => x.PrimitiveId).Distinct().OrderBy(x => x).ToArray(),
                chord.Value.LogicalX,
                chord.Value.Notes));
        }

        return result
            .OrderBy(x => x.MeasureNumber)
            .ThenBy(x => x.PhysicalBounds.Left)
            .ThenBy(x => x.PhysicalBounds.Top)
            .ToArray();
    }

    private IReadOnlyList<Fragment> BuildFragments(PrimitiveResolution primitives, LogicalGridResolution grid)
    {
        var result = new List<Fragment>();

        foreach (var primitive in primitives.Primitives)
        {
            if (primitive.MeasureNumber is not { } measureNumber)
                continue;
            if (primitive.Scope is not (PrimitiveLogicalScope.PartMeasure or PrimitiveLogicalScope.Measure))
                continue;

            var staffSpace = StaffSpaceFor(primitive, grid);
            if (staffSpace <= 1e-9)
                continue;

            var bounds = primitive.PhysicalBounds;
            if (bounds.Width <= 1e-9 || bounds.Height <= 1e-9)
                continue;
            if (bounds.Width / staffSpace > MaxWidthInStaffSpaces)
                continue;
            if (bounds.Height / staffSpace < MinFragmentHeightInStaffSpaces)
                continue;
            if (bounds.Height <= bounds.Width)
                continue;
            if (!LooksWavy(primitive.Contour, bounds, staffSpace))
                continue;

            result.Add(new Fragment(primitive.Id, measureNumber, bounds, staffSpace));
        }

        return result;
    }

    private bool LooksWavy(PrimitiveContour contour, RectD bounds, double staffSpace)
    {
        if (contour.Points.Count < 8)
            return false;

        var slices = Math.Clamp(SliceCount, 10, 64);
        var centers = new List<(double Y, double X)>();
        var sliceHeight = bounds.Height / slices;
        if (sliceHeight <= 1e-9)
            return false;

        for (var i = 0; i < slices; i++)
        {
            var top = bounds.Top + i * sliceHeight;
            var bottom = i == slices - 1 ? bounds.Bottom + 1e-9 : top + sliceHeight;
            var xs = contour.Points
                .Where(p => p.Y >= top && p.Y < bottom)
                .Select(p => (double)p.X)
                .ToArray();
            if (xs.Length == 0)
                continue;

            centers.Add(((top + bottom) / 2.0, (xs.Min() + xs.Max()) / 2.0));
        }

        if (centers.Count < 7)
            return false;

        var smoothed = new double[centers.Count];
        for (var i = 0; i < centers.Count; i++)
        {
            var from = Math.Max(0, i - 1);
            var to = Math.Min(centers.Count - 1, i + 1);
            var sum = 0d;
            for (var j = from; j <= to; j++)
                sum += centers[j].X;
            smoothed[i] = sum / (to - from + 1);
        }

        var minAmplitude = staffSpace * MinWaveAmplitudeInStaffSpaces;
        var directionChanges = 0;
        var lastSign = 0;
        var lastTurningX = smoothed[0];

        for (var i = 1; i < smoothed.Length; i++)
        {
            var dx = smoothed[i] - smoothed[i - 1];
            var deadBand = minAmplitude * 0.20;
            var sign = dx > deadBand ? 1 : dx < -deadBand ? -1 : 0;
            if (sign == 0)
                continue;

            if (lastSign != 0 && sign != lastSign)
            {
                if (Math.Abs(smoothed[i - 1] - lastTurningX) >= minAmplitude)
                {
                    directionChanges++;
                    lastTurningX = smoothed[i - 1];
                }
            }
            else if (lastSign == 0)
            {
                lastTurningX = smoothed[i - 1];
            }

            lastSign = sign;
        }

        var horizontalRange = smoothed.Max() - smoothed.Min();
        return directionChanges >= MinWaveDirectionChanges &&
               horizontalRange >= minAmplitude * 2.0;
    }

    private IReadOnlyList<IReadOnlyList<Fragment>> GroupAlignedFragments(IReadOnlyList<Fragment> fragments)
    {
        var result = new List<IReadOnlyList<Fragment>>();
        var remaining = new HashSet<int>(Enumerable.Range(0, fragments.Count));

        while (remaining.Count > 0)
        {
            var seedIndex = remaining.First();
            remaining.Remove(seedIndex);
            var groupIndices = new List<int> { seedIndex };
            var queue = new Queue<int>();
            queue.Enqueue(seedIndex);

            while (queue.Count > 0)
            {
                var currentIndex = queue.Dequeue();
                var current = fragments[currentIndex];
                var matches = remaining
                    .Where(i => CanJoin(current, fragments[i]))
                    .ToArray();

                foreach (var index in matches)
                {
                    remaining.Remove(index);
                    groupIndices.Add(index);
                    queue.Enqueue(index);
                }
            }

            result.Add(groupIndices.Select(i => fragments[i]).OrderBy(x => x.Bounds.Top).ToArray());
        }

        return result;
    }

    private bool CanJoin(Fragment a, Fragment b)
    {
        if (a.MeasureNumber != b.MeasureNumber)
            return false;

        var staffSpace = (a.StaffSpace + b.StaffSpace) / 2.0;
        if (Math.Abs(a.Bounds.CenterX - b.Bounds.CenterX) > staffSpace * MaxAlignedXDistanceInStaffSpaces)
            return false;

        var verticalGap = a.Bounds.Bottom < b.Bounds.Top
            ? b.Bounds.Top - a.Bounds.Bottom
            : b.Bounds.Bottom < a.Bounds.Top
                ? a.Bounds.Top - b.Bounds.Bottom
                : 0d;

        return verticalGap <= staffSpace * MaxFragmentGapInStaffSpaces;
    }

    private (double LogicalX, IReadOnlyList<NoteHeadResolution> Notes)? FindChordToRight(
        RectD arpeggio,
        int measureNumber,
        LogicalGridResolution grid,
        IReadOnlyList<NoteHeadResolution> noteHeads)
    {
        var measureNotes = noteHeads
            .Where(x => x.MeasureNumber == measureNumber)
            .Where(x => x.LogicalBounds.Left is not null && x.LogicalBounds.Right is not null)
            .ToArray();

        var columns = measureNotes
            .Select(x => new
            {
                Note = x,
                LogicalX = ((x.LogicalBounds.Left ?? 0d) + (x.LogicalBounds.Right ?? 0d)) / 2.0
            })
            .OrderBy(x => x.LogicalX)
            .ToArray();

        for (var i = 0; i < columns.Length; i++)
        {
            var logicalX = columns[i].LogicalX;
            var sameColumn = columns
                .Where(x => Math.Abs(x.LogicalX - logicalX) <= NoteColumnToleranceLogicalUnits)
                .Select(x => x.Note)
                .Distinct()
                .ToArray();
            if (sameColumn.Length < 2)
                continue;

            var acceptable = false;
            foreach (var note in sameColumn)
            {
                if (!grid.TryGetBlock(note.PartNumber, note.MeasureNumber, out var block))
                    continue;
                var arpeggioLogicalRight = block.ToLogical(new PointD(arpeggio.Right, arpeggio.CenterY)).X;
                if (arpeggioLogicalRight is not { } rightX)
                    continue;

                var distance = logicalX - rightX;
                if (distance >= -NoteColumnToleranceLogicalUnits && distance <= MaxNoteDistanceLogicalUnits)
                {
                    acceptable = true;
                    break;
                }
            }

            if (!acceptable)
                continue;

            if (sameColumn.Count(x => x.PhysicalBounds.CenterX >= arpeggio.Right) < 2)
                continue;

            return (logicalX, sameColumn);
        }

        return null;
    }

    private static double StaffSpaceFor(ResolvedPrimitive primitive, LogicalGridResolution grid)
    {
        if (primitive.PartNumber is { } part &&
            primitive.MeasureNumber is { } measure &&
            grid.TryGetBlock(part, measure, out var ownBlock))
            return ownBlock.PhysicalBounds.Height / 4.0;

        if (primitive.MeasureNumber is not { } measureNumber)
            return 0d;

        var blocks = grid.Blocks.Where(x => x.MeasureNumber == measureNumber).ToArray();
        return blocks.Length == 0 ? 0d : blocks.Average(x => x.PhysicalBounds.Height / 4.0);
    }

    private static RectD Union(IEnumerable<RectD> rectangles)
    {
        var items = rectangles.ToArray();
        return new RectD(
            items.Min(x => x.Left),
            items.Min(x => x.Top),
            items.Max(x => x.Right),
            items.Max(x => x.Bottom));
    }

    private sealed record Fragment(int PrimitiveId, int MeasureNumber, RectD Bounds, double StaffSpace);
}
