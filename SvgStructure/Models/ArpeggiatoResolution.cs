namespace SvgStructure.Models;

/// <summary>
/// A vertical arpeggiato mark immediately to the left of a chord.
/// One logical arpeggiato may be built from several aligned SVG primitives.
/// </summary>
public sealed record ArpeggiatoResolution(
    int MeasureNumber,
    RectD PhysicalBounds,
    IReadOnlyList<int> PrimitiveIds,
    double NoteLogicalX,
    IReadOnlyList<NoteHeadResolution> Notes);
