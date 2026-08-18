namespace SvgStructure.Models;

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
    IReadOnlyList<StemResolution> Stems);
