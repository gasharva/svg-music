namespace SvgStructure.Models;

public enum ClefKind
{
    G,
    F,
    C
}

public sealed record ClefResolution(
    int PartNumber,
    int MeasureNumber,
    ClefKind Kind,
    double Confidence,
    RectD PhysicalBounds,
    LogicalRectD LogicalBounds);
