namespace SvgStructure.Models;

/// <summary>A standalone note flag recognized next to the free endpoint of a stem.</summary>
public sealed record NoteFlagResolution(
    int PartNumber,
    int MeasureNumber,
    int Denominator,
    RectD PhysicalBounds,
    LogicalRectD LogicalBounds,
    StemResolution Stem,
    double Confidence);
