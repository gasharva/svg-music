namespace SvgStructure.Models;

public readonly record struct PointD(double X, double Y);

public readonly record struct LineSegment(PointD Start, PointD End)
{
    public double Width => Math.Abs(End.X - Start.X);
    public double Height => Math.Abs(End.Y - Start.Y);
    public double Left => Math.Min(Start.X, End.X);
    public double Right => Math.Max(Start.X, End.X);
    public double Top => Math.Min(Start.Y, End.Y);
    public double Bottom => Math.Max(Start.Y, End.Y);

    public bool IsHorizontal(double tolerance = 0.05) => Height <= tolerance;
    public bool IsVertical(double tolerance = 0.05) => Width <= tolerance;
}

public sealed record StaffSystem(
    double Left,
    double Right,
    double Top,
    double Bottom,
    int StaffCount,
    IReadOnlyList<double> BarXs);
