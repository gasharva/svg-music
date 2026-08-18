namespace SvgStructure.Models;

/// <summary>
/// One first-level beam joining two recognized stems. Endpoints follow the beam centerline at its
/// left and right edges. A level-1 beam is the primary beam nearest the free ends of the stems.
/// </summary>
public sealed record BeamResolution(
    int MeasureNumber,
    RectD PhysicalBounds,
    PointD LeftEndpoint,
    PointD RightEndpoint,
    StemResolution LeftStem,
    StemResolution RightStem,
    int Level = 1);
