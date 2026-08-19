namespace SvgStructure.Models;

/// <summary>
/// One PCA-recognized rest attached to a logical P+M staff position.
/// Denominator follows note-value notation: 1=whole, 2=half, 4=quarter, 8=eighth, 16=sixteenth, etc.
/// </summary>
public sealed record RestResolution(
    int PartNumber,
    int MeasureNumber,
    int Denominator,
    LogicalRectD LogicalBounds,
    RectD PhysicalBounds,
    double Confidence,
    int CandidateId);
