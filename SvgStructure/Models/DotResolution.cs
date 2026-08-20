namespace SvgStructure.Models;

/// <summary>
/// An augmentation dot recognized geometrically to the right of either a note head or a rest.
/// Exactly one of Note and Rest is populated.
/// </summary>
public sealed record DotResolution(
    int PrimitiveId,
    int PartNumber,
    int MeasureNumber,
    LogicalRectD LogicalBounds,
    RectD PhysicalBounds,
    NoteHeadResolution? Note,
    RestResolution? Rest)
{
    public RectD TargetPhysicalBounds => Note?.PhysicalBounds ?? Rest!.PhysicalBounds;
}
