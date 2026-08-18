namespace SvgStructure.Models;

/// <summary>
/// One geometrically recognized beam. Endpoints follow the beam centerline at its left and right
/// edges. Level 1 is the primary beam nearest the free stem ends; subsequent levels are secondary
/// beams whose stems are already covered by the previous level.
/// </summary>
public sealed record BeamResolution(
    int MeasureNumber,
    RectD PhysicalBounds,
    PointD LeftEndpoint,
    PointD RightEndpoint,
    int Level,
    IReadOnlyList<StemResolution> Stems,
    StemResolution? LeftStem = null,
    StemResolution? RightStem = null);
