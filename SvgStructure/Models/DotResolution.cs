namespace SvgStructure.Models;

/// <summary>
/// An augmentation dot recognized geometrically to the right of a note head.
/// Note is the note head whose duration the dot augments.
/// </summary>
public sealed record DotResolution(
    int PrimitiveId,
    int PartNumber,
    int MeasureNumber,
    LogicalRectD LogicalBounds,
    RectD PhysicalBounds,
    NoteHeadResolution Note);
