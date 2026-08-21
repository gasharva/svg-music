namespace SvgStructure.Models;

/// <summary>Logical part in reading order. Number is one-based.</summary>
public sealed record Part(int Number, string Id);

/// <summary>
/// Logical measure in score order. Number is one-based. StartsNewSystem means this measure begins
/// a new printed staff system in the source SVG and should become a MusicXML system break.
/// </summary>
public sealed record Measure(int Number, double Width, bool StartsNewSystem = false);

/// <summary>One logical Pn-Mm block mapped to its physical SVG rectangle.</summary>
public sealed record PartMeasureBlock(
    int PartNumber,
    int MeasureNumber,
    int SystemIndex,
    RectD PhysicalBounds)
{
    public string Label => $"P{PartNumber}-M{MeasureNumber}";
}

/// <summary>
/// Bridge between score coordinates (part/measure) and physical SVG coordinates.
/// LocalX/LocalY are normalized 0..1 coordinates inside a logical block and give us
/// room to add finer logical coordinates later without leaking SVG pixels everywhere.
/// </summary>
public sealed class PartMeasureMap
{
    private readonly IReadOnlyDictionary<(int Part, int Measure), PartMeasureBlock> _byLogicalCoordinate;

    public PartMeasureMap(IReadOnlyList<PartMeasureBlock> blocks, RectD pageBounds)
    {
        Blocks = blocks;
        PageBounds = pageBounds;
        _byLogicalCoordinate = blocks.ToDictionary(x => (x.PartNumber, x.MeasureNumber));
    }

    public IReadOnlyList<PartMeasureBlock> Blocks { get; }
    public RectD PageBounds { get; }

    public bool TryGetBlock(int partNumber, int measureNumber, out PartMeasureBlock block) =>
        _byLogicalCoordinate.TryGetValue((partNumber, measureNumber), out block!);

    public PartMeasureBlock GetBlock(int partNumber, int measureNumber) =>
        _byLogicalCoordinate[(partNumber, measureNumber)];

    public PointD ToPhysical(int partNumber, int measureNumber, double localX, double localY)
    {
        var block = GetBlock(partNumber, measureNumber);
        localX = Math.Clamp(localX, 0, 1);
        localY = Math.Clamp(localY, 0, 1);

        return new PointD(
            block.PhysicalBounds.Left + block.PhysicalBounds.Width * localX,
            block.PhysicalBounds.Top + block.PhysicalBounds.Height * localY);
    }

    public IReadOnlyList<PartMeasureBlock> GetMeasureBlocks(int measureNumber) =>
        Blocks.Where(x => x.MeasureNumber == measureNumber).OrderBy(x => x.PartNumber).ToArray();
}

public sealed record PartMeasureResolution(
    string SvgPath,
    IReadOnlyList<Part> Parts,
    IReadOnlyList<Measure> Measures,
    PartMeasureMap Map,
    int LineCount,
    int SystemCount);
