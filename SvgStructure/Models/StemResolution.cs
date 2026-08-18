namespace SvgStructure.Models;

public enum StemDirection
{
    Up,
    Down
}

/// <summary>
/// A thin vertical note stem whose upper or lower endpoint touches at least one recognized note head.
/// Direction describes which endpoint is attached: Up = lower endpoint touches the head,
/// Down = upper endpoint touches the head.
/// </summary>
public sealed record StemResolution(
    int PartNumber,
    int MeasureNumber,
    LogicalRectD LogicalBounds,
    RectD PhysicalBounds,
    StemDirection Direction,
    bool CrossStaff,
    IReadOnlyList<NoteHeadResolution> AttachedNotes);
