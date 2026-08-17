namespace SvgStructure.Models;

public sealed record LogicalPoint(double? X, double Y);

public sealed record LogicalRectD(
    double? Left,
    double Top,
    double? Right,
    double Bottom);

/// <summary>
/// Fine logical coordinate system inside one P+M block.
/// X is measured in beat subdivisions: 0..BeatNumber*SubdivisionsPerBeat.
/// Y is measured in half staff-spaces: top staff line = 0, next space = 1,
/// second line = 2, etc. Values above/below the staff are negative/greater than 8.
/// </summary>
public sealed record LogicalGridBlock(
    int PartNumber,
    int MeasureNumber,
    int? BeatNumber,
    int? BeatValue,
    int SubdivisionsPerBeat,
    RectD PhysicalBounds)
{
    public int? HorizontalUnits => BeatNumber * SubdivisionsPerBeat;
    public double HalfStaffSpace => PhysicalBounds.Height / 8d;

    public LogicalPoint ToLogical(PointD physical)
    {
        double? x = HorizontalUnits is { } units && PhysicalBounds.Width > 1e-9
            ? (physical.X - PhysicalBounds.Left) / PhysicalBounds.Width * units
            : null;

        var y = HalfStaffSpace > 1e-9
            ? (physical.Y - PhysicalBounds.Top) / HalfStaffSpace
            : 0d;

        return new LogicalPoint(x, y);
    }

    public LogicalRectD ToLogical(RectD physical)
    {
        var topLeft = ToLogical(new PointD(physical.Left, physical.Top));
        var bottomRight = ToLogical(new PointD(physical.Right, physical.Bottom));
        return new LogicalRectD(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    public PointD ToPhysical(LogicalPoint logical)
    {
        var x = logical.X is { } logicalX && HorizontalUnits is { } units && units > 0
            ? PhysicalBounds.Left + logicalX / units * PhysicalBounds.Width
            : PhysicalBounds.Left;

        var y = PhysicalBounds.Top + logical.Y * HalfStaffSpace;
        return new PointD(x, y);
    }

    public RectD ToPhysical(LogicalRectD logical)
    {
        var left = logical.Left is { } logicalLeft
            ? ToPhysical(new LogicalPoint(logicalLeft, logical.Top)).X
            : PhysicalBounds.Left;
        var right = logical.Right is { } logicalRight
            ? ToPhysical(new LogicalPoint(logicalRight, logical.Bottom)).X
            : PhysicalBounds.Right;
        var top = PhysicalBounds.Top + logical.Top * HalfStaffSpace;
        var bottom = PhysicalBounds.Top + logical.Bottom * HalfStaffSpace;

        return new RectD(left, top, right, bottom);
    }
}

public sealed class LogicalGridResolution
{
    private readonly IReadOnlyDictionary<(int Part, int Measure), LogicalGridBlock> _blocks;

    public LogicalGridResolution(IReadOnlyList<LogicalGridBlock> blocks)
    {
        Blocks = blocks;
        _blocks = blocks.ToDictionary(x => (x.PartNumber, x.MeasureNumber));
    }

    public IReadOnlyList<LogicalGridBlock> Blocks { get; }

    public bool TryGetBlock(int partNumber, int measureNumber, out LogicalGridBlock block) =>
        _blocks.TryGetValue((partNumber, measureNumber), out block!);

    public LogicalGridBlock GetBlock(int partNumber, int measureNumber) =>
        _blocks[(partNumber, measureNumber)];
}
