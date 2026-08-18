namespace SvgStructure.Models;

/// <summary>
/// One geometric oval candidate inspected by NoteHeadResolver. Kept only for diagnostics so the
/// step-by-step report can show the exact contour/group seen by the resolver and its final verdict.
/// </summary>
public sealed record NoteHeadDiagnosticEntry(
    int PrimitiveId,
    int PartNumber,
    int MeasureNumber,
    RectD PhysicalBounds,
    LogicalRectD LogicalBounds,
    PrimitiveContour Contour,
    IReadOnlyList<PrimitiveContour>? SourceGroupContours,
    bool Accepted,
    bool? IsFilled,
    string? Pitch,
    string Verdict,
    bool HollowContourDetected);
