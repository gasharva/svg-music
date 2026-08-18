namespace SvgStructure.Models;

public sealed record ArcDiagnosticEntry(
    int PrimitiveId,
    RectD PhysicalBounds,
    int ContourPointCount,
    double StaffSpace,
    string Stage,
    string Verdict,
    double? WidthInStaffSpaces = null,
    double? LeftThicknessInStaffSpaces = null,
    double? RightThicknessInStaffSpaces = null,
    double? CurvatureInStaffSpaces = null,
    PointD? LeftEndpoint = null,
    PointD? Midpoint = null,
    PointD? RightEndpoint = null,
    double? LeftNearestContactDistanceInStaffSpaces = null,
    double? RightNearestContactDistanceInStaffSpaces = null,
    int LeftContactCount = 0,
    int RightContactCount = 0,
    bool Accepted = false);
