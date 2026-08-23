namespace SvgStructure.Models;

public enum ArcCurveDirection { Up, Down }

/// <summary>
/// A curved score arc (tie/slur-like geometry) whose two ends terminate in proximity to recognized
/// note heads and/or free stem ends. The arc itself does not need to physically touch those objects.
/// </summary>
public sealed record ArcResolution(
    RectD PhysicalBounds,
    PointD LeftEndpoint,
    PointD Midpoint,
    PointD RightEndpoint,
    IReadOnlyList<NoteHeadResolution> Notes,
    IReadOnlyList<StemResolution> Stems)
{
    /// <summary>
    /// Visual bend of the resolved arc in SVG coordinates. Y grows downward, therefore a midpoint
    /// above the straight chord means the arc bends upward.
    /// </summary>
    public ArcCurveDirection CurveDirection =>
        Midpoint.Y < (LeftEndpoint.Y + RightEndpoint.Y) / 2.0
            ? ArcCurveDirection.Up
            : ArcCurveDirection.Down;
}
