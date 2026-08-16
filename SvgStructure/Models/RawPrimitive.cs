namespace SvgStructure.Models;

public readonly record struct RectD(double Left, double Top, double Right, double Bottom)
{
    public double Width => Math.Max(0, Right - Left);
    public double Height => Math.Max(0, Bottom - Top);
    public double CenterX => (Left + Right) / 2;
    public double CenterY => (Top + Bottom) / 2;

    public bool Intersects(RectD other) =>
        Right >= other.Left && Left <= other.Right &&
        Bottom >= other.Top && Top <= other.Bottom;

    public bool IntersectsHorizontally(double left, double right) =>
        Right >= left && Left <= right;
}

public sealed record RawPrimitive(
    int Id,
    RectD Bounds,
    PrimitiveContour Contour,
    string? SourceUseKey = null);

/// <summary>
/// One visual staff inside one measure: e.g. P2-M5.
/// PartIndex is zero-based internally; Label exposes human-friendly P1/P2 notation.
/// </summary>
public sealed record StaffMeasureRegion(
    int MeasureNumber,
    int SystemIndex,
    int PartIndex,
    double Left,
    double Right,
    double Top,
    double Bottom)
{
    public RectD Bounds => new(Left, Top, Right, Bottom);
    public double Height => Bottom - Top;
    public string Label => $"P{PartIndex + 1}-M{MeasureNumber}";
    public StaffMeasureKey Key => new(PartIndex, MeasureNumber);
}

public readonly record struct StaffMeasureKey(int PartIndex, int MeasureNumber)
{
    public override string ToString() => $"P{PartIndex + 1}-M{MeasureNumber}";
}
