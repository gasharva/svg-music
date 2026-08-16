namespace SvgStructure.Models;

public enum PrimitiveLogicalScope
{
    PartMeasure,
    Measure,
    PhysicalOnly
}

/// <summary>
/// A real content primitive from the SVG, enriched with logical score coordinates and its
/// physical vector contour. After PrimitiveResolver, later recognition must use this geometry
/// rather than reopening the source SVG.
/// </summary>
public sealed record ResolvedPrimitive(
    int Id,
    RectD PhysicalBounds,
    PrimitiveContour Contour,
    PrimitiveLogicalScope Scope,
    int? PartNumber,
    int? MeasureNumber,
    string? SourceUseKey = null,
    IReadOnlyList<PrimitiveContour>? SourceUseContours = null)
{
    public string LogicalLabel => Scope switch
    {
        PrimitiveLogicalScope.PartMeasure => $"P{PartNumber}-M{MeasureNumber}",
        PrimitiveLogicalScope.Measure => $"M{MeasureNumber}",
        _ => "physical-only"
    };
}

public sealed record PrimitiveResolution(
    PartMeasureResolution Structure,
    IReadOnlyList<ResolvedPrimitive> Primitives)
{
    public IReadOnlyList<ResolvedPrimitive> PartMeasurePrimitives =>
        Primitives.Where(x => x.Scope == PrimitiveLogicalScope.PartMeasure).ToArray();

    public IReadOnlyList<ResolvedPrimitive> MeasurePrimitives =>
        Primitives.Where(x => x.Scope == PrimitiveLogicalScope.Measure).ToArray();

    public IReadOnlyList<ResolvedPrimitive> PhysicalOnlyPrimitives =>
        Primitives.Where(x => x.Scope == PrimitiveLogicalScope.PhysicalOnly).ToArray();
}
