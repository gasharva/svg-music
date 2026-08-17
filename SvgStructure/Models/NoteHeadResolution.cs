namespace SvgStructure.Models;

/// <summary>
/// One geometrically recognized note head attached to a P+M logical block.
/// Pitch is the concert written pitch implied by logical staff position and the nearest clef to the left.
/// </summary>
public sealed record NoteHeadResolution(
    int PartNumber,
    int MeasureNumber,
    LogicalRectD LogicalBounds,
    RectD PhysicalBounds,
    bool IsFilled,
    string Pitch);
