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

public sealed record RawPrimitive(int Id, RectD Bounds);

public sealed record MeasureRegion(
    int Number,
    int SystemIndex,
    double Left,
    double Right,
    double Top,
    double Bottom)
{
    public RectD Bounds => new(Left, Top, Right, Bottom);
    public double Height => Bottom - Top;
}
