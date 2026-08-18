namespace SvgStructure.Models;

public enum AccidentalKind
{
    Flat,
    Sharp,
    Natural,
    DoubleSharp,
    DoubleFlat
}

/// <summary>
/// One recognized accidental on the logical P+M grid.
/// Note is null for an accidental that belongs to a key-signature-like prefix.
/// </summary>
public sealed record AccidentalResolution(
    int PartNumber,
    int MeasureNumber,
    LogicalRectD LogicalBounds,
    RectD PhysicalBounds,
    AccidentalKind Kind,
    double Confidence,
    NoteHeadResolution? Note);
